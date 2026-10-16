namespace Cato.Infrastructure.Redis;

/// <summary>Settings for the sweep that queues follower-history fetches.</summary>
public class FollowerHistorySweepSettings
{
    public const string SectionName = "FollowerHistorySweep";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// A game counts as "in a SteamDB list" when it has a list snapshot this recent.
    /// Not "today": the list scrape misses days (no activity file for 2026-10-14).
    /// </summary>
    public int ListLookbackDays { get; set; } = 7;

    /// <summary>
    /// Fetch a list game's graph again after this many days. Between fetches the
    /// daily list snapshot carries the same number (measured: 99.7% equal, 0-1 day
    /// apart), so this only repairs days the list scrape missed.
    /// </summary>
    public int ListRefreshAfterDays { get; set; } = 30;

    /// <summary>
    /// An Owned game in no SteamDB list is fetched again once its last fetch is this
    /// old: no list value carries it between fetches (56 of 68 Owned games on
    /// 2026-10-16). A listed Owned game follows <see cref="ListRefreshAfterDays"/>.
    /// </summary>
    public int OwnedRefreshAfterHours { get; set; } = 20;
}
