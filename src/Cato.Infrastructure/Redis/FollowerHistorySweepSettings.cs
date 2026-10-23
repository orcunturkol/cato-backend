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
}
