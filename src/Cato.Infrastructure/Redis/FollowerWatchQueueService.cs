using StackExchange.Redis;

namespace Cato.Infrastructure.Redis;

/// <summary>Redis reads and writes for the follower watch. See <see cref="FollowerWatchQueue"/>.</summary>
public interface IFollowerWatchQueueService
{
    /// <summary>Every member of a set with its score.</summary>
    Task<Dictionary<int, double>> GetAllAsync(string key, CancellationToken ct);

    /// <summary>The members of a set's quarantine.</summary>
    Task<HashSet<int>> GetQuarantinedAsync(string key, CancellationToken ct);

    /// <summary>Adds members that are not in the set; a member already in it keeps its score.</summary>
    Task AddAsync(string key, IReadOnlyList<KeyValuePair<int, double>> entries, CancellationToken ct);

    Task RemoveAsync(string key, IReadOnlyList<int> appIds, CancellationToken ct);
}

public sealed class FollowerWatchQueueService : IFollowerWatchQueueService
{
    // ZADD/ZREM arguments per call; the sets hold ~90k games.
    private const int ChunkSize = 1000;

    private readonly IConnectionMultiplexer _redis;

    public FollowerWatchQueueService(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<Dictionary<int, double>> GetAllAsync(string key, CancellationToken ct)
    {
        var result = new Dictionary<int, double>();
        // ZSCAN in pages, so one call does not block Redis for the whole set.
        await foreach (var entry in _redis.GetDatabase().SortedSetScanAsync(key, pageSize: ChunkSize).WithCancellation(ct))
        {
            if (int.TryParse(entry.Element.ToString(), out var appId))
                result[appId] = entry.Score;
        }
        return result;
    }

    public async Task<HashSet<int>> GetQuarantinedAsync(string key, CancellationToken ct) =>
        (await GetAllAsync(FollowerWatchQueue.QuarantineKey(key), ct)).Keys.ToHashSet();

    public async Task AddAsync(string key, IReadOnlyList<KeyValuePair<int, double>> entries, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        foreach (var chunk in entries.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            await db.SortedSetAddAsync(key, chunk.Select(e => new SortedSetEntry(e.Key, e.Value)).ToArray(), SortedSetWhen.NotExists);
        }
    }

    public async Task RemoveAsync(string key, IReadOnlyList<int> appIds, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        foreach (var chunk in appIds.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            await db.SortedSetRemoveAsync(key, chunk.Select(id => (RedisValue)id).ToArray());
        }
    }
}
