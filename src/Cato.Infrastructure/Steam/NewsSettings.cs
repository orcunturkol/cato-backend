namespace Cato.Infrastructure.Steam;

/// <summary>Settings for the owned-game news watcher (ISteamNews/GetNewsForApp, no key).</summary>
public class NewsSettings
{
    public const string SectionName = "News";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 360;

    /// <summary>News items asked for per game and cycle (the newest first).</summary>
    public int Count { get; set; } = 50;
}
