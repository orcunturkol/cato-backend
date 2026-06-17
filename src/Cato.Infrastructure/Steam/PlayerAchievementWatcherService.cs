using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Steam.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// Fetches per-reviewer acquired achievements (GetPlayerAchievements) for
/// public-profile authors of tracked games, then denormalizes the
/// "achievements unlocked at review time" metric onto steam_review.
///
/// Work queue is DB-driven: each cycle picks (AppId, SteamId64) pairs from
/// reviews JOIN public profiles, left-joined against steam_player_achievement_fetch,
/// least-recently-fetched first, skipping pairs still in back-off. The fetch
/// row both tracks the queue and disambiguates "fetched, none/private" from
/// "never fetched". GetPlayerAchievements costs one keyed API call per pair, so
/// this is the quota-dominant job — throttled by PlayerBatchSize / interval.
/// </summary>
public class PlayerAchievementWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerAchievementWatcherService> _logger;
    private readonly AchievementSettings _settings;
    private readonly SteamWebApiSettings _webApiSettings;

    public PlayerAchievementWatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<PlayerAchievementWatcherService> logger,
        IOptions<AchievementSettings> settings,
        IOptions<SteamWebApiSettings> webApiSettings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
        _webApiSettings = webApiSettings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Player achievement watcher disabled via Achievements:Enabled");
            return;
        }

        if (string.IsNullOrWhiteSpace(_webApiSettings.ApiKey))
        {
            _logger.LogWarning(
                "Player achievement watcher idle — no Steam Web API key configured (SteamWebApi:ApiKey / env SteamWebApi__ApiKey)");
            return;
        }

        // Brief startup delay to let the database finish migrating.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Player achievement fetch cycle failed");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(_settings.PlayerIntervalMinutes),
                stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var steamApi = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
        var tracker = scope.ServiceProvider.GetRequiredService<IJobRunTracker>();

        await using var job = await tracker.StartAsync("PlayerAchievementWatcher", ct: ct);
        try
        {
        var now = DateTime.UtcNow;
        var refreshCutoff = now.AddDays(-_settings.RefreshAfterDays);

        var pairs = await DequeuePairsAsync(db, now, refreshCutoff, ct);
        if (pairs.Count == 0)
        {
            job.Set("pairs", 0);
            _logger.LogInformation("Player achievement cycle: queue is empty, nothing to fetch");
            return;
        }

        var ok = 0;
        var priv = 0;
        var noStats = 0;
        var notOwned = 0;
        var error = 0;
        var skipped = 0;
        var reviewsUpdated = 0;

        // Cache schema counts per AppId for this cycle (the "Y" in "X of Y").
        var schemaCounts = new Dictionary<int, int>();

        foreach (var pair in pairs)
        {
            if (ct.IsCancellationRequested) break;

            var response = await steamApi.GetPlayerAchievementsAsync(pair.SteamId64, pair.AppId, ct);
            if (response is null)
            {
                // Transient API/network/auth failure — touch nothing, retried next cycle.
                skipped++;
                continue;
            }

            var fetch = await GetOrCreateFetchAsync(db, pair.SteamId64, pair.AppId, ct);

            if (!response.PlayerStats.Success)
            {
                var status = ClassifyError(response.PlayerStats.Error);
                ApplyFailure(fetch, status, now);
                switch (status)
                {
                    case AchievementFetchStatus.Private: priv++; break;
                    case AchievementFetchStatus.NoStats: noStats++; break;
                    case AchievementFetchStatus.NotOwned: notOwned++; break;
                    default: error++; break;
                }
                await db.SaveChangesAsync(ct);
                continue;
            }

            // Success: persist the acquired (unlocked) achievements.
            var achieved = response.PlayerStats.Achievements
                .Where(a => a.Achieved != 0 && !string.IsNullOrEmpty(a.ApiName))
                .ToList();

            await UpsertPlayerAchievementsAsync(db, pair.SteamId64, pair.AppId, achieved, now, ct);

            if (!schemaCounts.TryGetValue(pair.AppId, out var schemaCount))
            {
                schemaCount = await db.GameAchievementSchemas.CountAsync(a => a.AppId == pair.AppId, ct);
                schemaCounts[pair.AppId] = schemaCount;
            }

            fetch.Status = AchievementFetchStatus.Ok;
            fetch.AchievedCount = achieved.Count;
            fetch.SchemaCount = schemaCount;
            fetch.LastFetchedAt = now;
            fetch.QuarantinedUntil = null;
            fetch.ConsecutiveFailures = 0;

            reviewsUpdated += await RecomputeReviewMetricAsync(
                db, pair.SteamId64, pair.AppId, achieved, schemaCount, now, ct);

            ok++;
            await db.SaveChangesAsync(ct);
        }

        job.Set("pairs", pairs.Count);
        job.Set("ok", ok);
        job.Set("private", priv);
        job.Set("noStats", noStats);
        job.Set("notOwned", notOwned);
        job.Set("error", error);
        job.Set("skipped", skipped);
        job.Set("reviewsUpdated", reviewsUpdated);
        if (error > 0) job.MarkPartialSuccess();

        _logger.LogInformation(
            "Player achievement cycle complete: pairs={Pairs} ok={Ok} private={Private} noStats={NoStats} notOwned={NotOwned} error={Error} skipped={Skipped} reviewsUpdated={ReviewsUpdated}",
            pairs.Count, ok, priv, noStats, notOwned, error, skipped, reviewsUpdated);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }

    private async Task<List<PairKey>> DequeuePairsAsync(
        CatoDbContext db, DateTime now, DateTime refreshCutoff, CancellationToken ct)
    {
        // Distinct (AppId, SteamId64) for public-profile reviewers of tracked games.
        var candidates =
            (from r in db.SteamReviews
             where r.AuthorSteamId != null
             join p in db.SteamPlayerProfiles on r.AuthorSteamId equals (long?)p.SteamId64
             join g in db.Games on r.GameId equals g.Id
             where p.CommunityVisibilityState == 3
                   && (g.GameType == "Sourcing" || g.GameType == "Owned")
             select new { g.AppId, p.SteamId64 })
            .Distinct();

        // Left-join the fetch status; keep never-fetched, due-for-refresh, or expired back-off.
        var due =
            from c in candidates
            join f in db.SteamPlayerAchievementFetches
                on new { c.AppId, c.SteamId64 } equals new { f.AppId, f.SteamId64 } into fj
            from f in fj.DefaultIfEmpty()
            where f == null
                  || (f.QuarantinedUntil == null
                      && (f.LastFetchedAt == null || f.LastFetchedAt < refreshCutoff))
                  || (f.QuarantinedUntil != null && f.QuarantinedUntil <= now)
            // never-fetched (null) first, then oldest fetched.
            orderby f.LastFetchedAt ?? DateTime.MinValue
            select new PairKey { AppId = c.AppId, SteamId64 = c.SteamId64 };

        return await due.Take(_settings.PlayerBatchSize).ToListAsync(ct);
    }

    private static async Task<SteamPlayerAchievementFetch> GetOrCreateFetchAsync(
        CatoDbContext db, long steamId, int appId, CancellationToken ct)
    {
        var fetch = await db.SteamPlayerAchievementFetches
            .FirstOrDefaultAsync(f => f.SteamId64 == steamId && f.AppId == appId, ct);

        if (fetch is null)
        {
            fetch = db.SteamPlayerAchievementFetches.Add(new SteamPlayerAchievementFetch
            {
                Id = Guid.NewGuid(),
                SteamId64 = steamId,
                AppId = appId,
            }).Entity;
        }

        return fetch;
    }

    private void ApplyFailure(SteamPlayerAchievementFetch fetch, string status, DateTime now)
    {
        fetch.Status = status;
        fetch.LastFetchedAt = now;

        if (status is AchievementFetchStatus.Private
            or AchievementFetchStatus.NoStats
            or AchievementFetchStatus.NotOwned)
        {
            // Stable, near-permanent state — long back-off, not an error.
            fetch.ConsecutiveFailures = 0;
            fetch.QuarantinedUntil = now.AddHours(_settings.PrivateBackoffHours);
        }
        else
        {
            // Unclassified business error — short back-off, escalate after the threshold.
            fetch.ConsecutiveFailures++;
            fetch.QuarantinedUntil = fetch.ConsecutiveFailures >= _settings.FailureThreshold
                ? now.AddHours(_settings.PrivateBackoffHours)
                : now.AddMinutes(_settings.ErrorBackoffMinutes);
        }
    }

    private static async Task UpsertPlayerAchievementsAsync(
        CatoDbContext db,
        long steamId,
        int appId,
        List<SteamPlayerAchievementItem> achieved,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await db.SteamPlayerAchievements
            .Where(a => a.SteamId64 == steamId && a.AppId == appId)
            .ToDictionaryAsync(a => a.ApiName, ct);

        var keep = new HashSet<string>();

        foreach (var item in achieved)
        {
            var apiName = item.ApiName!;
            keep.Add(apiName);

            SteamPlayerAchievement entity;
            if (existing.TryGetValue(apiName, out var found))
            {
                entity = found;
            }
            else
            {
                entity = db.SteamPlayerAchievements.Add(new SteamPlayerAchievement
                {
                    Id = Guid.NewGuid(),
                    SteamId64 = steamId,
                    AppId = appId,
                    ApiName = apiName,
                }).Entity;
            }

            entity.UnlockTime = item.UnlockTime;
            entity.UnlockedAt = item.UnlockTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(item.UnlockTime).UtcDateTime
                : null;
        }

        // Remove rows that are no longer achieved (rare — achievements aren't usually revoked).
        foreach (var (apiName, entity) in existing)
        {
            if (!keep.Contains(apiName))
                db.SteamPlayerAchievements.Remove(entity);
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<int> RecomputeReviewMetricAsync(
        CatoDbContext db,
        long steamId,
        int appId,
        List<SteamPlayerAchievementItem> achieved,
        int schemaCount,
        DateTime now,
        CancellationToken ct)
    {
        // Unlock times we can place on a timeline (UnlockTime == 0 is excluded).
        var unlockTimes = achieved
            .Where(a => a.UnlockTime > 0)
            .Select(a => a.UnlockTime)
            .ToList();

        var reviews = await db.SteamReviews
            .Where(r => r.AuthorSteamId == steamId && r.Game.AppId == appId)
            .ToListAsync(ct);

        foreach (var review in reviews)
        {
            var reviewUnix = new DateTimeOffset(
                DateTime.SpecifyKind(review.TimestampCreated, DateTimeKind.Utc)).ToUnixTimeSeconds();

            review.AuthorAchievementsAtReview = unlockTimes.Count(u => u <= reviewUnix);
            review.GameAchievementCountAtFetch = schemaCount;
            review.AchievementsComputedAt = now;
        }

        return reviews.Count;
    }

    private static string ClassifyError(string? error)
    {
        if (string.IsNullOrEmpty(error)) return AchievementFetchStatus.Error;

        var e = error.ToLowerInvariant();
        if (e.Contains("not public")) return AchievementFetchStatus.Private;
        if (e.Contains("no stats")) return AchievementFetchStatus.NoStats;
        return AchievementFetchStatus.Error;
    }

    private sealed class PairKey
    {
        public int AppId { get; set; }
        public long SteamId64 { get; set; }
    }
}
