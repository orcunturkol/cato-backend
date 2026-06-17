using System.Text.Json.Serialization;

namespace Cato.Infrastructure.Steam.Models;

public class SteamPlayerSummariesResponse
{
    [JsonPropertyName("response")]
    public SteamPlayerSummariesInner Response { get; set; } = new();
}

public class SteamPlayerSummariesInner
{
    [JsonPropertyName("players")]
    public List<SteamPlayerSummary> Players { get; set; } = [];
}

public class SteamPlayerSummary
{
    [JsonPropertyName("steamid")]
    public string? SteamId { get; set; }

    /// <summary>1 = private, 2 = friends-only, 3 = public.</summary>
    [JsonPropertyName("communityvisibilitystate")]
    public int CommunityVisibilityState { get; set; }

    [JsonPropertyName("profilestate")]
    public int? ProfileState { get; set; }

    [JsonPropertyName("personaname")]
    public string? PersonaName { get; set; }

    [JsonPropertyName("profileurl")]
    public string? ProfileUrl { get; set; }

    [JsonPropertyName("avatarfull")]
    public string? AvatarFull { get; set; }

    [JsonPropertyName("avatarhash")]
    public string? AvatarHash { get; set; }

    [JsonPropertyName("personastate")]
    public int? PersonaState { get; set; }

    [JsonPropertyName("personastateflags")]
    public int? PersonaStateFlags { get; set; }

    [JsonPropertyName("realname")]
    public string? RealName { get; set; }

    [JsonPropertyName("primaryclanid")]
    public string? PrimaryClanId { get; set; }

    /// <summary>Unix timestamp; only present for public profiles.</summary>
    [JsonPropertyName("timecreated")]
    public long? TimeCreated { get; set; }

    [JsonPropertyName("lastlogoff")]
    public long? LastLogoff { get; set; }

    [JsonPropertyName("loccountrycode")]
    public string? LocCountryCode { get; set; }

    [JsonPropertyName("locstatecode")]
    public string? LocStateCode { get; set; }

    [JsonPropertyName("loccityid")]
    public int? LocCityId { get; set; }
}
