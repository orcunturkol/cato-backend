using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.Infrastructure.Redis;

/// <summary>The games the follower watch reads: new pages daily, the rest weekly.</summary>
public sealed record FollowerWatchCandidates(IReadOnlySet<int> Daily, IReadOnlySet<int> Weekly);

/// <summary>What one sweep changes in the two sets.</summary>
public sealed record FollowerWatchPlan(
    List<KeyValuePair<int, double>> AddDaily,
    List<KeyValuePair<int, double>> AddWeekly,
    List<int> RemoveDaily,
    List<int> RemoveWeekly);

/// <summary>
/// The follower watch: the Steam community-group member count of every game the "new pages"
/// list shows (not filtered, enriched) and the collectors do not track already. The tracked
/// games (<see cref="GameTypes.Tracked"/>) are read daily by group_member_count. The collector's
/// follower_watch job reads the daily set every day and a seventh of the weekly set, oldest
/// first. Scores: 0 = never read, positive = the last successful read (unix seconds), which
/// <c>RedisAppIdSource.mark_success</c> writes. Games without a community group end in
/// <c>{key}:quarantine</c> and are not added again.
/// </summary>
public static class FollowerWatchQueue
{
    public const string DailyKey = "steam:appids:follower_watch_daily";
    public const string WeeklyKey = "steam:appids:follower_watch_weekly";

    public static string QuarantineKey(string key) => key + ":quarantine";

    /// <summary>
    /// The writes that make the sets match the candidates. A new game is added at 0, so the
    /// next run reads it first. A game that moves between the sets (it passed
    /// <see cref="FollowerWatchSettings.DailyDays"/>) keeps its score, so it does not jump the
    /// weekly queue. A game that left the candidates (filtered, now tracked, deleted) is removed;
    /// a quarantined game is not added again.
    /// </summary>
    /// <param name="daily">Current members and scores of the daily set.</param>
    /// <param name="weekly">Current members and scores of the weekly set.</param>
    /// <param name="quarantined">Games in either set's quarantine.</param>
    public static FollowerWatchPlan Plan(
        FollowerWatchCandidates candidates,
        IReadOnlyDictionary<int, double> daily,
        IReadOnlyDictionary<int, double> weekly,
        IReadOnlySet<int> quarantined)
    {
        var addDaily = new List<KeyValuePair<int, double>>();
        var addWeekly = new List<KeyValuePair<int, double>>();

        foreach (var appId in candidates.Daily)
        {
            if (daily.ContainsKey(appId) || quarantined.Contains(appId))
                continue;
            addDaily.Add(new(appId, weekly.TryGetValue(appId, out var score) ? score : 0));
        }

        foreach (var appId in candidates.Weekly)
        {
            if (weekly.ContainsKey(appId) || quarantined.Contains(appId))
                continue;
            addWeekly.Add(new(appId, daily.TryGetValue(appId, out var score) ? score : 0));
        }

        var removeDaily = daily.Keys.Where(id => !candidates.Daily.Contains(id)).ToList();
        var removeWeekly = weekly.Keys.Where(id => !candidates.Weekly.Contains(id)).ToList();
        return new FollowerWatchPlan(addDaily, addWeekly, removeDaily, removeWeekly);
    }

    /// <summary>
    /// The "new pages" games (as <c>GET api/games/catalog</c> without app ids: not filtered,
    /// enriched) that are not tracked: added in the last <paramref name="dailyDays"/> days go to
    /// the daily set, older ones to the weekly set.
    /// </summary>
    public static async Task<FollowerWatchCandidates> LoadCandidatesAsync(
        CatoDbContext db, int dailyDays, DateTime now, CancellationToken ct = default)
    {
        var since = now.AddDays(-dailyDays);
        var games = await db.Games.AsNoTracking()
            .Where(g => !g.IsFiltered && g.LastEnrichedAt != null && g.AppId > 0
                        && !GameTypes.Tracked.Contains(g.GameType))
            .Select(g => new { g.AppId, g.CreatedAt })
            .ToListAsync(ct);

        var daily = new HashSet<int>();
        var weekly = new HashSet<int>();
        foreach (var g in games)
            (g.CreatedAt >= since ? daily : weekly).Add(g.AppId);
        weekly.ExceptWith(daily);
        return new FollowerWatchCandidates(daily, weekly);
    }
}
