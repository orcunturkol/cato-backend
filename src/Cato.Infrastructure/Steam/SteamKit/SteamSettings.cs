namespace Cato.Infrastructure.Steam.SteamKit;

public class SteamSettings
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>How often (in minutes) the PICS watcher polls for new games.</summary>
    public int PicsPollingIntervalMinutes { get; set; } = 10;
}
