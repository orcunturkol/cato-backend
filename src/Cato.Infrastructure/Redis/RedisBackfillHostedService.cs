using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cato.Infrastructure.Redis;

/// <summary>
/// Runs once at app startup. Reads tracked games (GameType IN 'Sourcing','Owned')
/// from Postgres and seeds the Redis sorted sets via ZADD NX 0. Idempotent:
/// existing scores are preserved (so orchestrator progress is not reset on
/// restart). Also self-heals after a Redis wipe or snapshot restore.
/// Failures are logged but don't block startup — Redis is best-effort infra.
/// </summary>
public class RedisBackfillHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBackfillHostedService> _logger;

    public RedisBackfillHostedService(
        IServiceProvider services,
        IConnectionMultiplexer redis,
        ILogger<RedisBackfillHostedService> logger)
    {
        _services = services;
        _redis = redis;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await BackfillAppIdsAsync(ct);
        await BackfillSteamIdRotationAsync(ct);
    }

    private async Task BackfillAppIdsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();

            var tracked = await db.Games
                .AsNoTracking()
                .Where(g => g.GameType == "Sourcing" || g.GameType == "Owned")
                .Select(g => new { g.AppId, g.Name })
                .ToListAsync(ct);

            if (tracked.Count == 0)
            {
                _logger.LogInformation("redis_backfill_skipped reason=\"no tracked games\"");
                return;
            }

            var redisDb = _redis.GetDatabase();

            var entries = tracked
                .Select(g => new SortedSetEntry(g.AppId, 0))
                .ToArray();

            foreach (var key in RedisAppIdSyncService.TrackedSortedSets)
                await redisDb.SortedSetAddAsync(key, entries, When.NotExists);

            var nameEntries = tracked
                .Select(g => new HashEntry(g.AppId.ToString(), g.Name ?? $"App {g.AppId}"))
                .ToArray();
            await redisDb.HashSetAsync(RedisAppIdSyncService.NameHashKey, nameEntries);

            _logger.LogInformation(
                "redis_backfill_complete count={Count} sets={Sets}",
                tracked.Count, RedisAppIdSyncService.TrackedSortedSets.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "redis_backfill_failed; orchestrators may see empty sorted sets until next restart");
        }
    }

    /// <summary>
    /// Rebuilds the reviewer-steamid rotation set from Postgres: known review
    /// authors (score 0) plus already-fetched profiles (score = LastFetchedAt,
    /// preserving rotation order). Quarantined ids are excluded — ZADD NX alone
    /// would resurrect them because quarantine removes them from the source set.
    /// </summary>
    private async Task BackfillSteamIdRotationAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
            var redisDb = _redis.GetDatabase();

            var quarantined = (await redisDb.SortedSetRangeByRankAsync(
                    RedisSteamIdRotationService.QuarantineKey, 0, -1))
                .Select(v => (long)v)
                .ToHashSet();

            var profileScores = await db.SteamPlayerProfiles
                .AsNoTracking()
                .Select(p => new { p.SteamId64, p.LastFetchedAt })
                .ToListAsync(ct);

            var fetchedIds = profileScores.Select(p => p.SteamId64).ToHashSet();

            var reviewerIds = await db.SteamReviews
                .AsNoTracking()
                .Where(r => r.AuthorSteamId != null)
                .Select(r => r.AuthorSteamId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var entries = profileScores
                .Where(p => !quarantined.Contains(p.SteamId64))
                .Select(p => new SortedSetEntry(
                    p.SteamId64, new DateTimeOffset(p.LastFetchedAt).ToUnixTimeSeconds()))
                .Concat(reviewerIds
                    .Where(id => !fetchedIds.Contains(id) && !quarantined.Contains(id))
                    .Select(id => new SortedSetEntry(id, 0)))
                .ToArray();

            if (entries.Length == 0)
            {
                _logger.LogInformation("redis_steamid_backfill_skipped reason=\"no known reviewer steamids\"");
                return;
            }

            await redisDb.SortedSetAddAsync(
                RedisSteamIdRotationService.SourceKey, entries, When.NotExists);

            _logger.LogInformation(
                "redis_steamid_backfill_complete count={Count} quarantined={Quarantined}",
                entries.Length, quarantined.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "redis_steamid_backfill_failed; profile watcher may see an empty rotation until next restart");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
