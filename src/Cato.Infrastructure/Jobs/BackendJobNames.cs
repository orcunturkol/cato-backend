namespace Cato.Infrastructure.Jobs;

/// <summary>Job names the backend watchers report under (job_run.JobName).</summary>
public static class BackendJobNames
{
    public const string SteamPicsWatcher = "SteamPicsWatcher";
    public const string SteamPicsChangeHistory = "SteamPicsChangeHistory";
    public const string SteamPriceWatcher = "SteamPriceWatcher";
    public const string SteamReviewWatcher = "SteamReviewWatcher";
    public const string GameEnrichmentWatcher = "GameEnrichmentWatcher";
    public const string GameAchievementSchemaWatcher = "GameAchievementSchemaWatcher";
    public const string PlayerAchievementWatcher = "PlayerAchievementWatcher";
    public const string SteamPlayerProfileWatcher = "SteamPlayerProfileWatcher";
}
