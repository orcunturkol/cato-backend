namespace Cato.Infrastructure.Steam;

/// <summary>Settings for the reviewer profile watcher (GetPlayerSummaries rotation).</summary>
public class PlayerProfileSettings
{
    public const string SectionName = "PlayerProfile";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 30;

    /// <summary>SteamIDs pulled from the rotation per cycle (chunked 100 per API call).</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Consecutive failures before a SteamID is quarantined.</summary>
    public int FailureThreshold { get; set; } = 5;
}
