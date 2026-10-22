using Cato.API.Models.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cato.API.Services.JobRuns;

public sealed record JobCatalogSnapshot(
    IReadOnlyList<JobDefinition> Jobs,
    IReadOnlyList<JobScheduleSourceDto> Sources)
{
    public JobDefinition? Find(string producer, string jobName) =>
        Jobs.FirstOrDefault(j => j.Producer == producer && j.JobName == jobName);

    public TimeSpan StaleAfterFor(string producer, string jobName) =>
        Find(producer, jobName)?.StaleAfter ?? JobRunRules.DefaultStaleAfter;
}

public interface IJobCatalog
{
    Task<JobCatalogSnapshot> LoadAsync(CancellationToken ct);
}

/// <summary>
/// Every known job: backend watchers from this API's own settings, collector jobs
/// from their scheduled_job rows.
/// </summary>
public sealed class JobCatalog : IJobCatalog
{
    private readonly CatoDbContext _db;
    private readonly SteamSettings _steam;
    private readonly GameEnrichmentSettings _enrichment;
    private readonly AchievementSettings _achievements;
    private readonly PlayerProfileSettings _profiles;
    private readonly SteamWebApiSettings _webApi;
    private readonly FollowerHistorySweepSettings _followerSweep;
    private readonly NewsSettings _news;
    // Watcher rows of scheduled_job by name, read by LoadAsync; they win over the settings.
    private IReadOnlyDictionary<string, ScheduledJob> _watcherRows = new Dictionary<string, ScheduledJob>();

    public JobCatalog(
        CatoDbContext db,
        IOptions<SteamSettings> steam,
        IOptions<GameEnrichmentSettings> enrichment,
        IOptions<AchievementSettings> achievements,
        IOptions<PlayerProfileSettings> profiles,
        IOptions<SteamWebApiSettings> webApi,
        IOptions<FollowerHistorySweepSettings> followerSweep,
        IOptions<NewsSettings> news)
    {
        _db = db;
        _steam = steam.Value;
        _enrichment = enrichment.Value;
        _achievements = achievements.Value;
        _profiles = profiles.Value;
        _webApi = webApi.Value;
        _followerSweep = followerSweep.Value;
        _news = news.Value;
    }

    public async Task<JobCatalogSnapshot> LoadAsync(CancellationToken ct)
    {
        _watcherRows = await _db.ScheduledJobs.AsNoTracking()
            .Where(j => j.ManagedBy == ScheduledJobManagedBy.Worker)
            .ToDictionaryAsync(j => j.Name, ct);
        var jobs = new List<JobDefinition>(BackendJobs());
        var sources = new List<JobScheduleSourceDto>();

        // Every collector job takes its schedule from scheduled_job, also a cron-managed one.
        var collector = await _db.ScheduledJobs.AsNoTracking()
            .Where(j => j.ManagedBy != ScheduledJobManagedBy.Worker)
            .OrderBy(j => j.Name)
            .ToListAsync(ct);
        if (collector.Count > 0)
        {
            jobs.AddRange(collector.Select(FromScheduledJob));
            sources.Add(new JobScheduleSourceDto(
                JobRunProducer.ExternalCollector, "dispatcher", null,
                DateTime.SpecifyKind(collector.Max(j => j.UpdatedAt), DateTimeKind.Utc), collector.Count));
        }

        return new JobCatalogSnapshot(jobs, sources);
    }

    public static JobDefinition FromScheduledJob(ScheduledJob job)
    {
        var expressions = ScheduledJobRules.SplitCron(job.Cron);
        return new JobDefinition(
            job.Name,
            JobRunProducer.ExternalCollector,
            job.Label,
            expressions.Count > 0 ? JobScheduleKind.Cron : JobScheduleKind.None,
            expressions,
            IntervalMinutes: null,
            job.Enabled,
            job.Enabled ? null : "paused on the Jobs page",
            job.TimeoutSeconds,
            JobRunRules.ForTimeout(job.TimeoutSeconds),
            TimeSpan.Zero);
    }

    /// <summary>
    /// Backend watchers. A watcher's scheduled_job row (written by the watcher itself on its
    /// first cycle) decides its interval and switch; until then its settings do. A missing
    /// Web API key still blocks the watchers that need it.
    /// </summary>
    public IEnumerable<JobDefinition> BackendJobs()
    {
        var hasWebApiKey = !string.IsNullOrWhiteSpace(_webApi.ApiKey);
        const string noKey = "no Steam Web API key configured";

        var keyBlock = hasWebApiKey ? null : noKey;

        yield return Interval(BackendJobNames.SteamPicsWatcher, "PICS new-game discovery",
            _steam.PicsPollingIntervalMinutes, TimeSpan.Zero, true, null);
        yield return Interval(BackendJobNames.SteamPicsChangeHistory, "PICS change history",
            _steam.PicsPollingIntervalMinutes, TimeSpan.Zero, true, null);
        yield return Interval(BackendJobNames.SteamPriceWatcher, "Store price check",
            _steam.PriceCheckIntervalHours * 60, TimeSpan.FromSeconds(30), true, null);
        yield return Interval(BackendJobNames.SteamReviewWatcher, "Store review sync",
            _steam.ReviewCheckIntervalHours * 60, TimeSpan.FromSeconds(10), true, null);
        yield return Interval(BackendJobNames.GameEnrichmentWatcher, "Store enrichment sweep",
            _enrichment.IntervalMinutes, TimeSpan.FromMinutes(1),
            _enrichment.Enabled, "GameEnrichment:Enabled is false");
        yield return Interval(BackendJobNames.GameAchievementSchemaWatcher, "Achievement schema fetch",
            _achievements.SchemaIntervalMinutes, TimeSpan.FromSeconds(10),
            _achievements.Enabled, "Achievements:Enabled is false", keyBlock);
        yield return Interval(BackendJobNames.GameNewsWatcher, "Owned game news fetch",
            _news.IntervalMinutes, TimeSpan.FromSeconds(20),
            _news.Enabled, "News:Enabled is false");
        yield return Interval(BackendJobNames.PlayerAchievementWatcher, "Player achievement fetch",
            _achievements.PlayerIntervalMinutes, TimeSpan.FromSeconds(10),
            _achievements.Enabled, "Achievements:Enabled is false", keyBlock);
        yield return Interval(BackendJobNames.SteamPlayerProfileWatcher, "Player profile fetch",
            _profiles.IntervalMinutes, TimeSpan.FromSeconds(10),
            _profiles.Enabled, "PlayerProfile:Enabled is false", keyBlock);
        yield return Interval(BackendJobNames.FollowerHistorySweep, "Follower history queue sweep",
            _followerSweep.IntervalMinutes, TimeSpan.FromMinutes(1),
            _followerSweep.Enabled, "FollowerHistorySweep:Enabled is false");
    }

    /// <param name="settingsReason">Why it is off when the settings switch it off.</param>
    /// <param name="blockedReason">Set when the watcher cannot run whatever its switch says.</param>
    private JobDefinition Interval(
        string name, string label, int intervalMinutes, TimeSpan initialDelay,
        bool settingsEnabled, string? settingsReason, string? blockedReason = null)
    {
        var on = settingsEnabled;
        var offReason = settingsReason;
        if (_watcherRows.TryGetValue(name, out var row))
        {
            on = row.Enabled;
            offReason = "paused on the Jobs page";
            intervalMinutes = row.IntervalMinutes ?? intervalMinutes;
        }
        return new(name, JobRunProducer.CatoBackend, label, JobScheduleKind.Interval, [], intervalMinutes,
            on && blockedReason is null, !on ? offReason : blockedReason, TimeoutSeconds: null,
            JobRunRules.ForInterval(TimeSpan.FromMinutes(Math.Max(intervalMinutes, 1))), initialDelay);
    }

}
