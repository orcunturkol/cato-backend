using Cato.Infrastructure.Steam;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Cato.Infrastructure.Redis;

/// <summary>
/// Key naming mirrors the Python orchestrators (redis_appid_source.py).
/// Failure/quarantine bookkeeping is intentionally sequential rather than a
/// Lua script: this key family has a single writer
/// (SteamPlayerProfileWatcherService), so atomicity is not required. Port to
/// IDatabase.ScriptEvaluate if a second writer ever appears.
/// </summary>
public class RedisSteamIdRotationService : ISteamIdRotationService
{
    public const string SourceKey = "steam:steamids:player_summaries";
    public const string FailuresKey = "steam:steamids:player_summaries:failures";
    public const string QuarantineKey = "steam:steamids:player_summaries:quarantine";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSteamIdRotationService> _logger;
    private readonly PlayerProfileSettings _settings;

    public RedisSteamIdRotationService(
        IConnectionMultiplexer redis,
        ILogger<RedisSteamIdRotationService> logger,
        IOptions<PlayerProfileSettings> settings)
    {
        _redis = redis;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task SeedAsync(IReadOnlyCollection<long> steamIds, CancellationToken ct)
    {
        if (steamIds.Count == 0) return;

        try
        {
            var db = _redis.GetDatabase();
            var entries = steamIds
                .Select(id => new SortedSetEntry(id, 0))
                .ToArray();
            await db.SortedSetAddAsync(SourceKey, entries, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "redis_steamid_seed_failed count={Count}", steamIds.Count);
        }
    }

    public async Task<IReadOnlyList<long>> FetchBatchAsync(int n, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        // Sorted-set rank order is score order, so rank 0..n-1 is the oldest-N.
        var values = await db.SortedSetRangeByRankAsync(SourceKey, 0, n - 1);

        var ids = new List<long>(values.Length);
        foreach (var value in values)
        {
            if (value.TryParse(out long id))
                ids.Add(id);
        }
        return ids;
    }

    public async Task MarkSuccessAsync(long steamId, DateTime fetchedAtUtc, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var score = new DateTimeOffset(fetchedAtUtc).ToUnixTimeSeconds();

        await db.SortedSetAddAsync(SourceKey, steamId, score, When.Exists);
        await db.HashDeleteAsync(FailuresKey, steamId);
    }

    public async Task<bool> MarkFailureAsync(long steamId, string reason, CancellationToken ct)
    {
        var db = _redis.GetDatabase();

        var failures = await db.HashIncrementAsync(FailuresKey, steamId);
        if (failures < _settings.FailureThreshold)
            return false;

        var nowTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await db.SortedSetAddAsync(QuarantineKey, steamId, nowTs);
        await db.SortedSetRemoveAsync(SourceKey, steamId);
        await db.HashDeleteAsync(FailuresKey, steamId);

        _logger.LogWarning(
            "steamid_quarantined steamId={SteamId} failures={Failures} reason={Reason}",
            steamId, failures, reason);
        return true;
    }
}
