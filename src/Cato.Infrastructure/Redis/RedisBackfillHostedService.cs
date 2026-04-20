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

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
