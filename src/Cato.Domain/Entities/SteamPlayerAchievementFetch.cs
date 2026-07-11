namespace Cato.Domain.Entities;

/// <summary>
/// Per-(SteamId64, AppId) fetch status for the player-achievement job. Doubles
/// as the DB work-queue (order by LastFetchedAt, skip rows in back-off) and
/// disambiguates "fetched, zero achievements / private" from "never fetched"
/// (which steam_player_achievement alone cannot express, since it stores only
/// unlocked rows). Latest-only upsert keyed by (SteamId64, AppId).
/// </summary>
public class SteamPlayerAchievementFetch
{
    public Guid Id { get; set; }

    public long SteamId64 { get; set; }
    public int AppId { get; set; }

    /// <summary>One of <see cref="AchievementFetchStatus"/>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Achieved-count at the last successful fetch (0 is meaningful — distinct from no row).</summary>
    public int AchievedCount { get; set; }

    /// <summary>The game's total achievement count snapshot at fetch time (for "X of Y").</summary>
    public int? SchemaCount { get; set; }

    public DateTime? LastFetchedAt { get; set; }

    /// <summary>Consecutive transient errors (drives the error back-off).</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// When set in the future, the pair is backed off and skipped by the queue
    /// until then. Private/no-stats/not-owned pairs back off long; transient
    /// errors back off short. Null = eligible for the normal refresh cadence.
    /// </summary>
    public DateTime? QuarantinedUntil { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Status values for <see cref="SteamPlayerAchievementFetch.Status"/> (string-const convention, like Game.GameType).</summary>
public static class AchievementFetchStatus
{
    /// <summary>Fetched successfully (AchievedCount may be 0).</summary>
    public const string Ok = "ok";

    /// <summary>Profile/game-details private — GetPlayerAchievements returned "Profile is not public".</summary>
    public const string Private = "private";

    /// <summary>The game exposes no achievement stats — "Requested app has no stats".</summary>
    public const string NoStats = "no_stats";

    /// <summary>Player does not own / has not played the game.</summary>
    public const string NotOwned = "not_owned";

    /// <summary>Unclassified failure (transient business error).</summary>
    public const string Error = "error";
}
