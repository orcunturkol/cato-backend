namespace Cato.Infrastructure.Steam.SteamKit;

public class SteamSettings
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>How often (in minutes) the PICS watcher polls for new games.</summary>
    public int PicsPollingIntervalMinutes { get; set; } = 10;

    /// <summary>How often (in hours) the price watcher checks for price changes.</summary>
    public int PriceCheckIntervalHours { get; set; } = 24;

    /// <summary>How often to poll Steam reviews for all tracked games (default 24 h).</summary>
    public int ReviewCheckIntervalHours { get; set; } = 24;

    /// <summary>Maximum individual reviews to backfill per game on first run (default 2000).</summary>
    public int ReviewBackfillMaxPerGame { get; set; } = 2000;
}
