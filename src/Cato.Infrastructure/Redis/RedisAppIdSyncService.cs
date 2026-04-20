using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cato.Infrastructure.Redis;

public class RedisAppIdSyncService : IRedisAppIdSyncService
{
    public const string NameHashKey = "steam:appid:names";

    public static readonly string[] TrackedSortedSets =
    {
        "steam:appids:ccu",
        "steam:appids:group_member_count",
        "steam:appids:steamdb_most_wished",
        "steam:appids:steamdb_wishlist_activity",
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisAppIdSyncService> _logger;

    public RedisAppIdSyncService(IConnectionMultiplexer redis, ILogger<RedisAppIdSyncService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task SyncAsync(int appId, string gameType, string? name, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();

            if (!string.IsNullOrWhiteSpace(name))
                await db.HashSetAsync(NameHashKey, appId.ToString(), name);

            if (!ShouldTrack(gameType)) return;

            var tasks = TrackedSortedSets
                .Select(key => db.SortedSetAddAsync(key, appId, 0, When.NotExists));
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "redis_sync_failed appId={AppId} gameType={GameType}", appId, gameType);
        }
    }

    public async Task UpdateAsync(int appId, string oldType, string newType, string? name, CancellationToken ct)
    {
        var wasTracked = ShouldTrack(oldType);
        var shouldTrack = ShouldTrack(newType);

        if (!wasTracked && shouldTrack)
        {
            await SyncAsync(appId, newType, name, ct);
        }
        else if (wasTracked && !shouldTrack)
        {
            await RemoveAsync(appId, ct);
        }
        else if (shouldTrack && !string.IsNullOrWhiteSpace(name))
        {
            // Still tracked, just refresh the name if one was supplied.
            try { await _redis.GetDatabase().HashSetAsync(NameHashKey, appId.ToString(), name); }
            catch (Exception ex) { _logger.LogWarning(ex, "redis_name_refresh_failed appId={AppId}", appId); }
        }
    }

    public async Task RemoveAsync(int appId, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var removals = TrackedSortedSets
                .Select(key => db.SortedSetRemoveAsync(key, appId))
                .Cast<Task>()
                .ToList();
            removals.Add(db.HashDeleteAsync(NameHashKey, appId.ToString()));
            await Task.WhenAll(removals);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "redis_remove_failed appId={AppId}", appId);
        }
    }

    /// <summary>
    /// Matches the legacy <c>db_game_provider.py</c> filter:
    /// <c>WHERE "GameType" = 'Sourcing' OR "GameType" = 'Owned'</c>.
    /// </summary>
    public static bool ShouldTrack(string gameType) =>
        gameType is "Sourcing" or "Owned";
}
