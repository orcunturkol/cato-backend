namespace Cato.Infrastructure.Redis;

/// <summary>Settings for the sweep that fills the follower-watch sets.</summary>
public class FollowerWatchSettings
{
    public const string SectionName = "FollowerWatch";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 360;

    /// <summary>A game added to CATO this many days ago or less is read daily, an older one weekly.</summary>
    public int DailyDays { get; set; } = 90;
}
