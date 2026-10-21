using System.Text.Json;
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
/// Every known job: backend watchers from this API's own settings, external jobs
/// from the schedule their producer last published.
/// </summary>
public sealed class JobCatalog : IJobCatalog
{
    internal static readonly JsonSerializerOptions EntryJson = new(JsonSerializerDefaults.Web);

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

        var snapshots = await _db.JobScheduleSnapshots.AsNoTracking()
            .OrderBy(s => s.Producer)
            .ToListAsync(ct);

        foreach (var snapshot in snapshots)
        {
            var entries = ParseEntries(snapshot.EntriesJson);
            sources.Add(new JobScheduleSourceDto(
                snapshot.Producer, snapshot.Source, snapshot.SourceHost, snapshot.PublishedAt, entries.Count));
            jobs.AddRange(FromEntries(snapshot.Producer, entries));
        }

        // A job the dispatcher starts takes its schedule from scheduled_job, not the crontab.
        var dispatched = await _db.ScheduledJobs.AsNoTracking()
            .Where(j => j.ManagedBy == ScheduledJobManagedBy.Dispatcher)
            .OrderBy(j => j.Name)
            .ToListAsync(ct);
        if (dispatched.Count > 0)
        {
            var names = dispatched.Select(j => j.Name).ToHashSet();
            jobs.RemoveAll(j => j.Producer == JobRunProducer.ExternalCollector && names.Contains(j.JobName));
            jobs.AddRange(dispatched.Select(FromScheduledJob));
            sources.Add(new JobScheduleSourceDto(
                JobRunProducer.ExternalCollector, "dispatcher", null,
                DateTime.SpecifyKind(dispatched.Max(j => j.UpdatedAt), DateTimeKind.Utc), dispatched.Count));
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

    internal static List<JobScheduleEntryDto> ParseEntries(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<JobScheduleEntryDto>>(json, EntryJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>One definition per job name; several cron lines for one job merge.</summary>
    internal static IEnumerable<JobDefinition> FromEntries(string producer, IEnumerable<JobScheduleEntryDto> entries)
    {
        foreach (var group in entries.GroupBy(e => e.JobName))
        {
            var all = group.ToList();
            var active = all.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Cron)).ToList();
            var shown = active.Count > 0 ? active : all.Where(e => !string.IsNullOrWhiteSpace(e.Cron)).ToList();
            var timeout = all.Select(e => e.TimeoutSeconds).FirstOrDefault(t => t is > 0);

            yield return new JobDefinition(
                group.Key,
                producer,
                all.Select(e => e.Label).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)),
                JobScheduleKind.Cron,
                shown.Select(e => e.Cron!.Trim()).Distinct().ToList(),
                IntervalMinutes: null,
                Enabled: active.Count > 0,
                DisabledReason: active.Count > 0
                    ? null
                    : all.Select(e => e.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "not scheduled",
                timeout,
                JobRunRules.ForTimeout(timeout),
                TimeSpan.Zero);
        }
    }
}
