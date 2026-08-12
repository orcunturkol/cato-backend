using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cato.Infrastructure.Redis;

public class RedisAppIdSyncService : IRedisAppIdSyncService
{
    public const string NameHashKey = "steam:appid:names";
    public const string FollowerHistoryKey = "steam:appids:steamdb_follower_history";

    public static readonly string[] TrackedSortedSets =
    {
        "steam:appids:ccu",
        "steam:appids:group_member_count",
        "steam:appids:steamdb_most_wished",
        "steam:appids:steamdb_wishlist_activity",
        FollowerHistoryKey,
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

    /// <remarks>
    /// Unlike every other method here, this one lets exceptions escape. The others
    /// are best-effort adjuncts to work that already succeeded, but this write is
    /// the entire point of the message that triggered it: a reddit-analysed game is
    /// stubbed as GameType "Other", which <see cref="ShouldTrack"/> excludes, so it
    /// is in no tracked set and this negative-score entry is the only thing that
    /// will ever queue it. Swallowing a failure here would mean the game silently
    /// never gets its follower history. Letting it throw dead-letters the message
    /// so it can be replayed.
    /// </remarks>
    public async Task PrioritizeFollowerHistoryAsync(int appId, DateTimeOffset analyzedAt, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        // Never resurrect an appid the orchestrator's failure threshold booted.
        if (await db.SortedSetScoreAsync($"{FollowerHistoryKey}:quarantine", appId) is not null)
        {
            _logger.LogInformation("follower_priority_skipped_quarantined appId={AppId}", appId);
            return;
        }

        // Score semantics: negative = prioritised, 0 = seeded but never fetched,
        // positive = last successful fetch. Only the first two may be lowered —
        // a game already backfilled must not be dragged back to the front.
        // (ZADD LT is not a substitute: -epoch is less than any positive score,
        // so LT would happily demote an already-fetched game.)
        var current = await db.SortedSetScoreAsync(FollowerHistoryKey, appId);
        if (current is > 0)
        {
            _logger.LogDebug(
                "follower_priority_skipped_already_fetched appId={AppId} score={Score}",
                appId, current);
            return;
        }

        // Negated so a more recent analysis sorts first within the band.
        await db.SortedSetAddAsync(FollowerHistoryKey, appId, -analyzedAt.ToUnixTimeSeconds());

        _logger.LogInformation("follower_priority_set appId={AppId}", appId);
    }

    /// <summary>
    /// Matches the legacy <c>db_game_provider.py</c> filter:
    /// <c>WHERE "GameType" = 'Sourcing' OR "GameType" = 'Owned'</c>.
    /// </summary>
    public static bool ShouldTrack(string gameType) =>
        gameType is "Sourcing" or "Owned";
}
