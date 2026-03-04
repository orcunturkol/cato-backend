using System.Text.Json.Serialization;

namespace Cato.Infrastructure.Steam.Models;

/// <summary>
/// Deserialization models for the Steam Store API appdetails endpoint.
/// </summary>
public class SteamAppDetailsWrapper
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public SteamAppData? Data { get; set; }
}

public class SteamAppData
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("steam_appid")]
    public int? SteamAppId { get; set; }

    [JsonPropertyName("is_free")]
    public bool? IsFree { get; set; }

    [JsonPropertyName("short_description")]
    public string? ShortDescription { get; set; }

    [JsonPropertyName("detailed_description")]
    public string? DetailedDescription { get; set; }

    [JsonPropertyName("header_image")]
    public string? HeaderImage { get; set; }

    [JsonPropertyName("capsule_image")]
    public string? CapsuleImage { get; set; }

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("developers")]
    public List<string>? Developers { get; set; }

    [JsonPropertyName("publishers")]
    public List<string>? Publishers { get; set; }

    [JsonPropertyName("price_overview")]
    public SteamPriceOverview? PriceOverview { get; set; }

    [JsonPropertyName("platforms")]
    public SteamPlatforms? Platforms { get; set; }

    [JsonPropertyName("genres")]
    public List<SteamGenre>? Genres { get; set; }

    [JsonPropertyName("categories")]
    public List<SteamCategory>? Categories { get; set; }

    [JsonPropertyName("release_date")]
    public SteamReleaseDate? ReleaseDate { get; set; }

    [JsonPropertyName("supported_languages")]
    public string? SupportedLanguages { get; set; }

    [JsonPropertyName("metacritic")]
    public SteamMetacritic? Metacritic { get; set; }
}

public class SteamPriceOverview
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("initial")]
    public int? Initial { get; set; }

    [JsonPropertyName("final")]
    public int? Final { get; set; }

    [JsonPropertyName("discount_percent")]
    public int? DiscountPercent { get; set; }

    /// <summary>Price in USD (derived from "final" cents / 100).</summary>
    public decimal? FinalUsd => Final.HasValue ? Final.Value / 100m : null;
}

public class SteamPlatforms
{
    [JsonPropertyName("windows")]
    public bool Windows { get; set; }

    [JsonPropertyName("mac")]
    public bool Mac { get; set; }

    [JsonPropertyName("linux")]
    public bool Linux { get; set; }
}

public class SteamGenre
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class SteamCategory
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class SteamReleaseDate
{
    [JsonPropertyName("coming_soon")]
    public bool? ComingSoon { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}

public class SteamMetacritic
{
    [JsonPropertyName("score")]
    public int? Score { get; set; }
}
