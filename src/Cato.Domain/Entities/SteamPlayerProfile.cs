namespace Cato.Domain.Entities;

/// <summary>
/// Latest-known Steam community profile for a reviewer (latest-only upsert, no
/// snapshot history). Sourced from ISteamUser/GetPlayerSummaries/v2.
/// CommunityVisibilityState == 3 means public — the future achievements job
/// (GetPlayerAchievements) must target only those rows.
/// </summary>
public class SteamPlayerProfile
{
    public Guid Id { get; set; }

    /// <summary>
    /// SteamID64. Steam serializes it as a JSON string but it is a 64-bit
    /// numeric id — stored as bigint for a compact unique index and typed
    /// joins against steam_review.AuthorSteamId.
    /// </summary>
    public long SteamId64 { get; set; }

    /// <summary>1 = private, 2 = friends-only, 3 = public.</summary>
    public int CommunityVisibilityState { get; set; }

    /// <summary>1 if the user has set up a community profile.</summary>
    public int? ProfileState { get; set; }

    public string PersonaName { get; set; } = string.Empty;
    public string? ProfileUrl { get; set; }
    public string? AvatarHash { get; set; }
    public string? AvatarFullUrl { get; set; }

    public int? PersonaState { get; set; }
    public int? PersonaStateFlags { get; set; }
    public string? RealName { get; set; }
    public string? PrimaryClanId { get; set; }

    /// <summary>Account creation time (only exposed for public profiles).</summary>
    public DateTime? TimeCreated { get; set; }

    public DateTime? LastLogoff { get; set; }

    public string? LocCountryCode { get; set; }
    public string? LocStateCode { get; set; }
    public int? LocCityId { get; set; }

    /// <summary>When this profile was last successfully fetched from Steam.</summary>
    public DateTime LastFetchedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
