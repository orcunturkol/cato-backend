using StackExchange.Redis;

namespace Cato.Infrastructure.Redis;

/// <summary>Redis reads and writes for the follower-history queue. See <see cref="FollowerHistoryQueue"/>.</summary>
public interface IFollowerHistoryQueueService
{
    /// <summary>
    /// Put an app id at the front of the queue. Unlike the sweep this always
    /// writes: someone asked for this game, so a fetched game is fetched again and
    /// a quarantined one gets a fresh failure budget. Throws on Redis failure.
    /// </summary>
    Task<FollowerHistoryState> RequestAsync(int appId, DateTimeOffset now, CancellationToken ct);

    Task<FollowerHistoryState> GetStateAsync(int appId, CancellationToken ct);

    /// <summary>Current scores of the app ids that are in the queue.</summary>
    Task<Dictionary<int, double>> GetScoresAsync(IReadOnlyList<int> appIds, CancellationToken ct);

    Task<HashSet<int>> GetQuarantinedAsync(IReadOnlyList<int> appIds, CancellationToken ct);

    Task SetScoresAsync(IReadOnlyList<KeyValuePair<int, double>> scores, CancellationToken ct);

    /// <summary>Requests not yet fetched, for any app id.</summary>
    Task<long> CountRequestsAsync(CancellationToken ct);

    /// <summary>App ids waiting in the SteamDB-list band (between <c>ListBase</c> and 0).</summary>
    Task<List<int>> GetListBandAsync(CancellationToken ct);

    /// <summary>Take app ids out of the queue; quarantine and failure counts stay.</summary>
    Task RemoveAsync(IReadOnlyList<int> appIds, CancellationToken ct);
}

public sealed class FollowerHistoryQueueService : IFollowerHistoryQueueService
{
    // ZMSCORE/ZADD arguments per call; a sweep covers ~12k list games.
    private const int ChunkSize = 1000;

    private readonly IConnectionMultiplexer _redis;

    public FollowerHistoryQueueService(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<FollowerHistoryState> RequestAsync(int appId, DateTimeOffset now, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var score = FollowerHistoryQueue.RequestScore(now);

        var tx = db.CreateTransaction();
        _ = tx.SortedSetRemoveAsync(FollowerHistoryQueue.QuarantineKey, appId);
        _ = tx.HashDeleteAsync(FollowerHistoryQueue.FailuresKey, appId);
        _ = tx.SortedSetAddAsync(FollowerHistoryQueue.Key, appId, score);
        await tx.ExecuteAsync();

        return FollowerHistoryQueue.Describe(score, null);
    }

    public async Task<FollowerHistoryState> GetStateAsync(int appId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var score = db.SortedSetScoreAsync(FollowerHistoryQueue.Key, appId);
        var quarantinedAt = db.SortedSetScoreAsync(FollowerHistoryQueue.QuarantineKey, appId);
        return FollowerHistoryQueue.Describe(await score, await quarantinedAt);
    }

    public async Task<Dictionary<int, double>> GetScoresAsync(IReadOnlyList<int> appIds, CancellationToken ct)
    {
        var result = new Dictionary<int, double>();
        await foreach (var (appId, score) in ScoresAsync(FollowerHistoryQueue.Key, appIds, ct))
            result[appId] = score;
        return result;
    }

    public async Task<HashSet<int>> GetQuarantinedAsync(IReadOnlyList<int> appIds, CancellationToken ct)
    {
        var result = new HashSet<int>();
        await foreach (var (appId, _) in ScoresAsync(FollowerHistoryQueue.QuarantineKey, appIds, ct))
            result.Add(appId);
        return result;
    }

    public async Task SetScoresAsync(IReadOnlyList<KeyValuePair<int, double>> scores, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        foreach (var chunk in scores.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            var entries = chunk.Select(s => new SortedSetEntry(s.Key, s.Value)).ToArray();
            await db.SortedSetAddAsync(FollowerHistoryQueue.Key, entries);
        }
    }

    public Task<long> CountRequestsAsync(CancellationToken ct) =>
        _redis.GetDatabase().SortedSetLengthAsync(
            FollowerHistoryQueue.Key, double.NegativeInfinity, FollowerHistoryQueue.RequestBandMax, Exclude.Stop);

    public async Task<List<int>> GetListBandAsync(CancellationToken ct)
    {
        var members = await _redis.GetDatabase().SortedSetRangeByScoreAsync(
            FollowerHistoryQueue.Key, FollowerHistoryQueue.ListBase, 0, Exclude.Both);
        return members.Select(m => (int)m).ToList();
    }

    public async Task RemoveAsync(IReadOnlyList<int> appIds, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        foreach (var chunk in appIds.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            await db.SortedSetRemoveAsync(FollowerHistoryQueue.Key, chunk.Select(id => (RedisValue)id).ToArray());
        }
    }

    private async IAsyncEnumerable<(int AppId, double Score)> ScoresAsync(
        string key, IReadOnlyList<int> appIds, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        foreach (var chunk in appIds.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            var members = chunk.Select(id => (RedisValue)id).ToArray();
            var scores = await db.SortedSetScoresAsync(key, members);
            for (var i = 0; i < chunk.Length; i++)
            {
                if (scores[i] is { } score)
                    yield return (chunk[i], score);
            }
        }
    }
}
