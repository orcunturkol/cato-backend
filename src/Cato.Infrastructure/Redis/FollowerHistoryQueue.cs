using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.Infrastructure.Redis;

/// <summary>Where a game stands in the follower-history queue, read from its Redis score.</summary>
/// <param name="Status">requested, queued, fetched, quarantined or not_queued.</param>
public sealed record FollowerHistoryState(
    string Status,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? LastFetchedAt,
    DateTimeOffset? QuarantinedAt);

/// <summary>
/// Backfill state after a sweep, for the Jobs page. Done = fetched and not due again;
/// waiting = never fetched or queued. An Owned game counts once, as Owned, also when
/// it is in a list. Quarantined games are in neither count.
/// </summary>
public sealed record FollowerHistoryProgress(
    int OwnedDone,
    int OwnedWaiting,
    int ListedDone,
    int ListedWaiting);

/// <summary>Owned games and the best recent SteamDB list rank of every listed game.</summary>
public sealed record FollowerHistoryCandidates(
    IReadOnlySet<int> Owned,
    IReadOnlyDictionary<int, int> ListRanks)
{
    public IEnumerable<int> AppIds => Owned.Union(ListRanks.Keys);
}

/// <summary>
/// Score bands of <c>steam:appids:steamdb_follower_history</c>. The collector reads
/// it with ZRANGE ascending, so a lower score is fetched sooner:
///
/// <code>
///   &lt; -1e9        requested (button or API), at -epoch: newest request first
///   -2e6          Owned game due (daily when in no list, else every 30 days)
///   -1e6 + rank   SteamDB list game, best rank first
///   0             seeded by RedisAppIdSyncService, never fetched
///   &gt; 0           last successful fetch, epoch seconds (written by the collector)
/// </code>
///
/// The collector fetches everything at or below 0 and always fetches a negative
/// score fresh from SteamDB. It never revisits the positive band on its own: a game
/// goes back into a negative band only through a request or this sweep.
/// </summary>
public static class FollowerHistoryQueue
{
    public const string Key = RedisAppIdSyncService.FollowerHistoryKey;
    public const string QuarantineKey = Key + ":quarantine";
    public const string FailuresKey = Key + ":failures";

    /// <summary>Every score below this is a request. The collector's <c>--priority-only</c>
    /// pass reads exactly this band, so the value is mirrored there.</summary>
    public const double RequestBandMax = -1_000_000_000;

    public const double OwnedScore = -2_000_000;

    public const double ListBase = -1_000_000;

    /// <summary>
    /// Game types whose follower history SteamDB fills from the lists. Until 2026-10-23 every
    /// list game was queued: ~13k games, most of them tracked by nobody, which cost the paid
    /// proxy its traffic. A request (button or API) still works for any game.
    /// </summary>
    public static readonly string[] TrackedTypes = ["Sourcing", "Owned", "Competitor"];

    public static double RequestScore(DateTimeOffset requestedAt) => -requestedAt.ToUnixTimeSeconds();

    public static double ListScore(int rank) => ListBase + Math.Clamp(rank, 1, 999_999);

    public static FollowerHistoryState Describe(double? score, double? quarantinedAt)
    {
        if (quarantinedAt is { } q)
            return new("quarantined", null, null, DateTimeOffset.FromUnixTimeSeconds((long)q));

        return score switch
        {
            null => new("not_queued", null, null, null),
            < RequestBandMax => new("requested", DateTimeOffset.FromUnixTimeSeconds((long)-score.Value), null, null),
            <= 0 => new("queued", null, null, null),
            _ => new("fetched", null, DateTimeOffset.FromUnixTimeSeconds((long)score.Value), null),
        };
    }

    /// <summary>
    /// The score writes one sweep makes. A game is (re)queued when it was never
    /// fetched or its last fetch is older than its refresh age. Requests and
    /// quarantined games are left alone, and a score already at its target is not
    /// written again.
    /// </summary>
    /// <param name="scores">Current scores of the candidates that are in the set.</param>
    public static List<KeyValuePair<int, double>> Plan(
        FollowerHistoryCandidates candidates,
        IReadOnlyDictionary<int, double> scores,
        IReadOnlySet<int> quarantined,
        DateTimeOffset now,
        FollowerHistorySweepSettings settings)
    {
        var writes = new List<KeyValuePair<int, double>>();

        foreach (var appId in candidates.AppIds)
        {
            if (quarantined.Contains(appId))
                continue;

            var owned = candidates.Owned.Contains(appId);
            var listed = candidates.ListRanks.TryGetValue(appId, out var rank);
            var target = owned ? OwnedScore : ListScore(rank);
            // A listed game gets its daily value from the list; only an Owned game in
            // no list needs the daily fetch.
            var refreshAfter = owned && !listed
                ? TimeSpan.FromHours(settings.OwnedRefreshAfterHours)
                : TimeSpan.FromDays(settings.ListRefreshAfterDays);

            if (scores.TryGetValue(appId, out var current))
            {
                if (current < RequestBandMax)
                    continue;
                if (current > 0 && now - DateTimeOffset.FromUnixTimeSeconds((long)current) < refreshAfter)
                    continue;
                if (current == target)
                    continue;
            }

            writes.Add(new(appId, target));
        }

        return writes;
    }

    /// <param name="scores">Scores after the sweep's writes.</param>
    public static FollowerHistoryProgress Progress(
        FollowerHistoryCandidates candidates,
        IReadOnlyDictionary<int, double> scores,
        IReadOnlySet<int> quarantined)
    {
        int ownedDone = 0, ownedWaiting = 0, listedDone = 0, listedWaiting = 0;

        foreach (var appId in candidates.AppIds)
        {
            if (quarantined.Contains(appId))
                continue;

            var done = scores.TryGetValue(appId, out var score) && score > 0;
            if (candidates.Owned.Contains(appId))
            {
                if (done) ownedDone++;
                else ownedWaiting++;
            }
            else if (done) listedDone++;
            else listedWaiting++;
        }

        return new FollowerHistoryProgress(ownedDone, ownedWaiting, listedDone, listedWaiting);
    }

    public static async Task<FollowerHistoryCandidates> LoadCandidatesAsync(
        CatoDbContext db, FollowerHistorySweepSettings settings, DateOnly today, CancellationToken ct = default)
    {
        var owned = await db.Games.AsNoTracking()
            .Where(g => g.GameType == "Owned")
            .Select(g => g.AppId)
            .ToListAsync(ct);

        var since = today.AddDays(-settings.ListLookbackDays);
        var ranks = await db.SteamDbSnapshots.AsNoTracking()
            .Where(s => s.SnapshotDate >= since && TrackedTypes.Contains(s.Game.GameType))
            .GroupBy(s => s.Game.AppId)
            .Select(g => new { AppId = g.Key, Rank = g.Min(s => s.Rank) })
            .ToDictionaryAsync(x => x.AppId, x => x.Rank, ct);

        return new FollowerHistoryCandidates(owned.ToHashSet(), ranks);
    }

    /// <summary>App ids of every game with a <see cref="TrackedTypes"/> type.</summary>
    public static async Task<HashSet<int>> LoadTrackedAsync(CatoDbContext db, CancellationToken ct = default) =>
        (await db.Games.AsNoTracking()
            .Where(g => TrackedTypes.Contains(g.GameType))
            .Select(g => g.AppId)
            .ToListAsync(ct)).ToHashSet();
}
