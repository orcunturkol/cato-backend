using System.Text.Json.Serialization;

namespace Cato.Infrastructure.Steam.Models;

/// <summary>
/// ISteamUserStats/GetSchemaForGame/v2 response. The achievements array is
/// absent/empty for games that define no achievements — that is a valid result,
/// not an error.
/// </summary>
public class SteamSchemaForGameResponse
{
    [JsonPropertyName("game")]
    public SteamSchemaGame Game { get; set; } = new();
}

public class SteamSchemaGame
{
    [JsonPropertyName("gameName")]
    public string? GameName { get; set; }

    [JsonPropertyName("availableGameStats")]
    public SteamAvailableGameStats? AvailableGameStats { get; set; }
}

public class SteamAvailableGameStats
{
    [JsonPropertyName("achievements")]
    public List<SteamSchemaAchievement> Achievements { get; set; } = [];
}

public class SteamSchemaAchievement
{
    /// <summary>Internal achievement key (matches GetPlayerAchievements' apiname).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>0 = visible, 1 = hidden.</summary>
    [JsonPropertyName("hidden")]
    public int Hidden { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("icongray")]
    public string? IconGray { get; set; }
}
