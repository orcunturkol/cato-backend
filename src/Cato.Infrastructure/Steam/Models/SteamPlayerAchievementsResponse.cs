using System.Text.Json.Serialization;

namespace Cato.Infrastructure.Steam.Models;

/// <summary>
/// ISteamUserStats/GetPlayerAchievements/v1 response. On a 2xx, inspect
/// PlayerStats.Success: false means a per-pair business outcome (private
/// profile, no stats, not owned) carried in Error — NOT a transient failure.
/// </summary>
public class SteamPlayerAchievementsResponse
{
    [JsonPropertyName("playerstats")]
    public SteamPlayerStats PlayerStats { get; set; } = new();
}

public class SteamPlayerStats
{
    [JsonPropertyName("steamID")]
    public string? SteamId { get; set; }

    [JsonPropertyName("gameName")]
    public string? GameName { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Set when Success is false, e.g. "Profile is not public" / "Requested app has no stats".</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("achievements")]
    public List<SteamPlayerAchievementItem> Achievements { get; set; } = [];
}

public class SteamPlayerAchievementItem
{
    [JsonPropertyName("apiname")]
    public string? ApiName { get; set; }

    /// <summary>1 = unlocked, 0 = locked.</summary>
    [JsonPropertyName("achieved")]
    public int Achieved { get; set; }

    /// <summary>Unix seconds when unlocked; 0 if unknown/locked.</summary>
    [JsonPropertyName("unlocktime")]
    public long UnlockTime { get; set; }
}
