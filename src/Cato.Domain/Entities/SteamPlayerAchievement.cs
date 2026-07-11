namespace Cato.Domain.Entities;

/// <summary>
/// A single achievement a reviewer has UNLOCKED (achieved == 1 only), sourced
/// from ISteamUserStats/GetPlayerAchievements/v1. The numerator data for the
/// "achievements at review time" metric. Linked to the game's achievement
/// catalog via <see cref="GameAchievementSchemaId"/> (FK to
/// <see cref="GameAchievementSchema"/>) rather than duplicating AppId/ApiName.
/// Keyed by (SteamId64, GameAchievementSchemaId); latest-only upsert.
/// </summary>
public class SteamPlayerAchievement
{
    public Guid Id { get; set; }

    public long SteamId64 { get; set; }

    /// <summary>FK to GameAchievementSchema.Id — the catalog row this unlock refers to.</summary>
    public Guid GameAchievementSchemaId { get; set; }

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

    public GameAchievementSchema GameAchievementSchema { get; set; } = null!;
}
