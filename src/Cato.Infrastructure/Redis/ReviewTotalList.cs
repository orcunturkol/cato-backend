using StackExchange.Redis;

namespace Cato.Infrastructure.Redis;

/// <summary>
/// The games whose review totals CATO keeps although it does not sync their reviews in full
/// (see <c>ReviewTotalWatcherService</c>). Its one owner replaces the whole list: OGT sends the
/// games of its closed evaluations once a day. <see cref="RedisBackfillHostedService"/> does not
/// rebuild this key, so after a Redis loss the list stays empty until the owner sends it again.
/// </summary>
public interface IReviewTotalList
{
    /// <summary>Replaces the list in one step; returns how many app ids it holds now.</summary>
    Task<int> ReplaceAsync(IReadOnlyCollection<int> appIds, CancellationToken ct);

    Task<IReadOnlyList<int>> GetAsync(CancellationToken ct);
}

public sealed class ReviewTotalList(IConnectionMultiplexer redis) : IReviewTotalList
{
    // A plain set, outside steam:appids:*: those sorted sets belong to the collector.
    public const string Key = "cato:review_totals:appids";

    public async Task<int> ReplaceAsync(IReadOnlyCollection<int> appIds, CancellationToken ct)
    {
        var members = appIds.Distinct().Select(id => (RedisValue)id).ToArray();

        // One transaction, so a reader never sees half a list.
        var tx = redis.GetDatabase().CreateTransaction();
        _ = tx.KeyDeleteAsync(Key);
        if (members.Length > 0)
            _ = tx.SetAddAsync(Key, members);
        await tx.ExecuteAsync();
        return members.Length;
    }

    public async Task<IReadOnlyList<int>> GetAsync(CancellationToken ct) =>
        (await redis.GetDatabase().SetMembersAsync(Key)).Select(v => (int)v).ToList();
}
