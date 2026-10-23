using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.JobRuns;

/// <summary>
/// One job's data check: the newest value of a date column in the table the job writes.
/// A date column (no time) counts from its midnight UTC. Typed queries, not SQL text, so
/// nothing editable can run in the database.
/// </summary>
public sealed record FreshnessCheck(
    string JobName,
    string Producer,
    string Table,
    string Column,
    TimeSpan MaxAge,
    Func<CatoDbContext, CancellationToken, Task<DateTime?>> NewestAsync);

public sealed record JobFreshnessDto(
    string JobName,
    string Producer,
    string Table,
    string Column,
    DateTime? NewestAt,
    double MaxAgeHours,
    double? AgeHours,
    bool Stale,
    bool Enabled,
    string? Error,
    long DurationMs = 0);

/// <summary>
/// Finds a job that reports Succeeded but writes nothing. Columns are chosen to be indexed
/// or in a small table, because the alert monitor runs these every 10 minutes.
/// </summary>
public static class JobFreshnessChecks
{
    private const string Collector = JobRunProducer.ExternalCollector;
    private const string Backend = JobRunProducer.CatoBackend;

    private static DateTime? Midnight(DateOnly? day) => day?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static readonly IReadOnlyList<FreshnessCheck> All =
    [
        // Twice a day (00:00 and 12:00 UTC since 2026-10-23; every 4 h before); a run takes ~3 min.
        new("ccu", Collector, "ccu_history", "Timestamp", TimeSpan.FromHours(12.5),
            async (db, ct) => await db.CcuHistories.MaxAsync(e => (DateTime?)e.Timestamp, ct)),
        // Daily jobs write today's date; two days from its midnight covers one missed run's slack.
        new("group_member_count", Collector, "group_member_count_snapshot", "SnapshotDate (Steam group)", TimeSpan.FromDays(2),
            async (db, ct) => Midnight(await db.GroupMemberCountSnapshots
                .Where(e => e.Source == GroupMemberCountSnapshot.SteamCommunitySource)
                .MaxAsync(e => (DateOnly?)e.SnapshotDate, ct))),
        new("steamdb_followers", Collector, "group_member_count_snapshot", "SnapshotDate (SteamDB followers)", TimeSpan.FromDays(2),
            async (db, ct) => Midnight(await db.GroupMemberCountSnapshots
                .Where(e => e.Source == GroupMemberCountSnapshot.SteamDbFollowerSource)
                .MaxAsync(e => (DateOnly?)e.SnapshotDate, ct))),
        new("steamdb", Collector, "steamdb_snapshot", "SnapshotDate", TimeSpan.FromDays(2),
            async (db, ct) => Midnight(await db.SteamDbSnapshots.MaxAsync(e => (DateOnly?)e.SnapshotDate, ct))),
        // Steam reports sales a day or so late.
        new("financial", Collector, "steam_sale_financial", "SaleDate", TimeSpan.FromDays(3),
            async (db, ct) => Midnight(await db.SteamSaleFinancials.MaxAsync(e => (DateOnly?)e.SaleDate, ct))),
        new("special_events", Collector, "steam_special_event", "LastSeenAt", TimeSpan.FromHours(26),
            async (db, ct) => await db.SteamSpecialEvents.MaxAsync(e => (DateTime?)e.LastSeenAt, ct)),
        // The partner site has data up to yesterday (Pacific, for the wishlist).
        new("steamworks_wishlist", Collector, "steamworks_wishlist_daily", "DateLocal", TimeSpan.FromDays(3),
            async (db, ct) => Midnight(await db.SteamworksWishlistDaily.MaxAsync(e => (DateOnly?)e.DateLocal, ct))),
        new("steamworks_traffic", Collector, "steam_traffic_breakdown", "PeriodEnd", TimeSpan.FromDays(3),
            async (db, ct) => Midnight(await db.SteamTrafficBreakdowns.MaxAsync(e => (DateOnly?)e.PeriodEnd, ct))),
        new("steamworks_navstats", Collector, "steamworks_nav_stat", "PeriodEnd", TimeSpan.FromDays(3),
            async (db, ct) => Midnight(await db.SteamworksNavStats.MaxAsync(e => (DateOnly?)e.PeriodEnd, ct))),
        new("steamworks_players", Collector, "steamworks_app_details", "SnapshotDate", TimeSpan.FromDays(2),
            async (db, ct) => Midnight(await db.SteamworksAppDetails.MaxAsync(e => (DateOnly?)e.SnapshotDate, ct))),
        new("steamworks_demo_downloads", Collector, "demo_download", "PeriodEnd", TimeSpan.FromDays(3),
            async (db, ct) => Midnight(await db.DemoDownloads.MaxAsync(e => (DateOnly?)e.PeriodEnd, ct))),
        // Every 24 h after the previous check ends; a check takes a few hours at most.
        new(BackendJobNames.SteamPriceWatcher, Backend, "price_snapshot", "CapturedAt", TimeSpan.FromHours(30),
            async (db, ct) => await db.PriceSnapshots.MaxAsync(e => (DateTime?)e.CapturedAt, ct)),
    ];
}

/// <summary>Runs the checks; a job that is paused or disabled is reported but never stale.</summary>
public sealed class JobFreshnessService
{
    private readonly CatoDbContext _db;
    private readonly IJobCatalog _catalog;
    private readonly ILogger<JobFreshnessService> _logger;

    public JobFreshnessService(CatoDbContext db, IJobCatalog catalog, ILogger<JobFreshnessService> logger)
    {
        _db = db;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<List<JobFreshnessDto>> CheckAsync(DateTime nowUtc, CancellationToken ct)
    {
        var catalog = await _catalog.LoadAsync(ct);
        var results = new List<JobFreshnessDto>(JobFreshnessChecks.All.Count);
        foreach (var check in JobFreshnessChecks.All)
        {
            var enabled = catalog.Find(check.Producer, check.JobName)?.Enabled ?? false;
            DateTime? newest = null;
            string? error = null;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                newest = await check.NewestAsync(_db, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Freshness check {Job} failed: {Error}", check.JobName, ex.Message);
                error = ex.Message;
            }

            var age = newest is { } n ? nowUtc - DateTime.SpecifyKind(n, DateTimeKind.Utc) : (TimeSpan?)null;
            var stale = enabled && error is null && (age is null || age > check.MaxAge);
            results.Add(new JobFreshnessDto(
                check.JobName, check.Producer, check.Table, check.Column,
                newest is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null,
                check.MaxAge.TotalHours, age?.TotalHours is { } h ? Math.Round(h, 1) : null,
                stale, enabled, error,
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }
        return results;
    }
}

public interface IJobFreshnessSource
{
    /// <summary>Check results at most <see cref="JobFreshnessCache.MaxAge"/> old.</summary>
    Task<(DateTime CheckedAt, List<JobFreshnessDto> Results)> GetAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>The endpoint's and the alert monitor's view: the cache, refreshed by a new check when old.</summary>
public sealed class CachedJobFreshness(JobFreshnessService service, JobFreshnessCache cache) : IJobFreshnessSource
{
    public Task<(DateTime CheckedAt, List<JobFreshnessDto> Results)> GetAsync(DateTime nowUtc, CancellationToken ct) =>
        cache.GetAsync(service, nowUtc, ct);
}

/// <summary>The last check results, shared by the endpoint and the alert monitor (singleton).</summary>
public sealed class JobFreshnessCache
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTime At, List<JobFreshnessDto> Results)? _last;

    /// <summary>Cached results, or new ones when the cache is older than <see cref="MaxAge"/>.</summary>
    public async Task<(DateTime CheckedAt, List<JobFreshnessDto> Results)> GetAsync(
        JobFreshnessService service, DateTime nowUtc, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_last is not { } last || nowUtc - last.At >= MaxAge)
            {
                _last = (nowUtc, await service.CheckAsync(nowUtc, ct));
                last = _last.Value;
            }
            return (last.At, last.Results);
        }
        finally
        {
            _lock.Release();
        }
    }
}
