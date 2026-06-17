namespace Cato.Domain.Entities;

/// <summary>
/// A single achievement a reviewer has UNLOCKED (achieved == 1 only), sourced
/// from ISteamUserStats/GetPlayerAchievements/v1. The numerator data for the
/// "achievements at review time" metric. Value-joined (no FK): SteamId64 ↔
/// steam_review.AuthorSteamId, AppId ↔ main_game.AppId. Keyed by
/// (SteamId64, AppId, ApiName); latest-only upsert.
/// </summary>
public class SteamPlayerAchievement
{
    public Guid Id { get; set; }

    public long SteamId64 { get; set; }
    public int AppId { get; set; }

    /// <summary>Steam internal achievement key; matches GameAchievementSchema.ApiName.</summary>
    public string ApiName { get; set; } = string.Empty;

    /// <summary>
    /// Unix seconds when unlocked. Can be 0 for very old unlocks predating
    /// Steam's per-achievement timestamping — those are excluded from the
    /// at-review count (we cannot place them in time) but still counted as owned.
    /// </summary>
    public long UnlockTime { get; set; }

    /// <summary>Convenience: UnlockTime as UTC, or null when UnlockTime is 0.</summary>
    public DateTime? UnlockedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
