namespace Cato.Infrastructure.Steam;

/// <summary>
/// Settings for the achievement watchers (GetSchemaForGame + GetPlayerAchievements).
/// Reuses the shared SteamWebApiSettings.ApiKey. GetPlayerAchievements costs one
/// keyed API call PER (game, reviewer) pair, so PlayerBatchSize / PlayerIntervalMinutes
/// are the throttle against the ~100k/day key budget.
/// </summary>
public class AchievementSettings
{
    public const string SectionName = "Achievements";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the per-game achievement schema is refreshed (schemas change rarely).</summary>
    public int SchemaIntervalMinutes { get; set; } = 360;

    /// <summary>How often a player-achievement cycle runs.</summary>
    public int PlayerIntervalMinutes { get; set; } = 10;

    /// <summary>(game, reviewer) pairs fetched per cycle. Equals API calls per cycle (1 call per pair).</summary>
    public int PlayerBatchSize { get; set; } = 300;

    /// <summary>How stale a successfully-fetched pair may get before it is refreshed.</summary>
    public int RefreshAfterDays { get; set; } = 30;

    /// <summary>Consecutive transient errors before a pair is treated as failing.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Back-off for private / no-stats / not-owned pairs (a long, near-permanent skip).</summary>
    public int PrivateBackoffHours { get; set; } = 168;

    /// <summary>Back-off for transient per-pair errors.</summary>
    public int ErrorBackoffMinutes { get; set; } = 60;
}
