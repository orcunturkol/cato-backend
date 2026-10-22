using Cato.API.Models.JobRuns;
using Cato.API.Services.Handlers.JobRuns;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Jobs;

namespace Cato.API.Tests.JobRuns;

public class JobRunsOverviewTests : JobRunsDbTestBase
{
    private const string Collector = JobRunProducer.ExternalCollector;

    // Time limit 0: no limit, so the default stale window applies.
    private static ScheduledJob[] CollectorSchedule() =>
    [
        CollectorJob("ccu", "CCU", "0 */4 * * *"),
        CollectorJob("steamdb", "SteamDB rankings", "30 3 * * *", 3600),
        CollectorJob("financial", "Partner financials", "0 4 * * *", enabled: false),
        CollectorJob("steamdb_followers", "SteamDB followers", "0 6 * * *", 7200),
        CollectorJob("steamdb_followers_priority", "SteamDB followers, priority", "15 * * * *", 7200),
    ];

    private static ScheduledJob CollectorJob(
        string name, string label, string cron, int timeoutSeconds = 0, bool enabled = true) => new()
    {
        Name = name, Label = label, AgentGroup = JobAgentGroups.Light, Module = $"orchestrators.run_{name}",
        Cron = cron, Enabled = enabled, TimeoutSeconds = timeoutSeconds,
        ManagedBy = ScheduledJobManagedBy.Dispatcher, UpdatedAt = Now, UpdatedBy = "seed",
    };

    private async Task SeedScheduleAsync()
    {
        await using var db = NewContext();
        db.ScheduledJobs.AddRange(CollectorSchedule());
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync(params JobRun[] runs)
    {
        await using var db = NewContext();
        db.JobRuns.AddRange(runs);
        await db.SaveChangesAsync();
    }

    private static JobRun Run(
        string job, string status, DateTime start, DateTime? end = null, string producer = Collector) => new()
    {
        Id = Guid.NewGuid(),
        JobName = job,
        Producer = producer,
        Status = status,
        StartTime = start,
        EndTime = end,
        DurationMs = end is null ? null : (long)(end.Value - start).TotalMilliseconds,
    };

    private async Task<JobRunsOverviewDto> OverviewAsync(bool runsWatchers = true)
    {
        await using var db = NewContext();
        var catalog = NewCatalog(db);
        var handler = new GetJobRunsOverviewHandler(
            db, catalog, new JobRunStatusResolver(db, WorkerStart(db, runsWatchers), Clock), Process,
            WorkerStart(db, runsWatchers), Clock);
        return await handler.Handle(new GetJobRunsOverviewQuery(), CancellationToken.None);
    }

    private async Task RecordWorkerStartAsync(DateTime started)
    {
        await using var db = NewContext();
        db.WorkerStates.Add(new WorkerState
        {
            Key = WorkerStateKeys.WorkerStartedAt,
            Value = started.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            UpdatedAt = started,
        });
        await db.SaveChangesAsync();
    }

    private static JobRun PriceCycle(DateTime start) =>
        Run(BackendJobNames.SteamPriceWatcher, JobRunStatus.Running, start, producer: JobRunProducer.CatoBackend);

    [Fact]
    public async Task An_api_only_restart_leaves_the_workers_runs_running()
    {
        // Role api: this process started at ApiStart; the worker has run for two hours.
        await SeedAsync(PriceCycle(ApiStart.AddMinutes(-20)));
        await RecordWorkerStartAsync(ApiStart.AddHours(-2));

        var overview = await OverviewAsync(runsWatchers: false);

        var price = overview.Jobs.Single(j => j.JobName == BackendJobNames.SteamPriceWatcher);
        Assert.Equal(JobRunStatus.Running, price.LastRun!.EffectiveStatus);
        Assert.Equal(ApiStart.AddHours(-2), overview.WorkerStartedAt);
    }

    [Fact]
    public async Task A_worker_restart_ends_the_runs_it_interrupted()
    {
        await SeedAsync(PriceCycle(ApiStart.AddMinutes(-20)));
        await RecordWorkerStartAsync(ApiStart.AddMinutes(-5));

        var overview = await OverviewAsync(runsWatchers: false);

        var price = overview.Jobs.Single(j => j.JobName == BackendJobNames.SteamPriceWatcher);
        Assert.Equal(JobRunStatus.Lost, price.LastRun!.EffectiveStatus);
        Assert.Equal(JobRunStatusEvaluator.ReasonWorkerRestarted, price.LastRun.LostReason);
    }

    [Fact]
    public async Task Without_a_recorded_worker_start_no_run_is_lost_by_a_restart()
    {
        await SeedAsync(PriceCycle(ApiStart.AddMinutes(-20)));

        var overview = await OverviewAsync(runsWatchers: false);

        Assert.Equal(JobRunStatus.Running,
            overview.Jobs.Single(j => j.JobName == BackendJobNames.SteamPriceWatcher).LastRun!.EffectiveStatus);
        Assert.Null(overview.WorkerStartedAt);
    }

    [Fact]
    public async Task The_worker_records_its_start_for_api_processes()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped(services, _ => NewContext());
        var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(
                Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services));

        await new WorkerStartRecorder(scopes, Process, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkerStartRecorder>.Instance)
            .StartAsync(default);

        await using var db = NewContext();
        Assert.Equal(ApiStart, await WorkerStart(db, runsWatchers: false).GetAsync(default));
    }

    [Fact]
    public async Task Overview_lists_backend_and_scheduled_collector_jobs()
    {
        await SeedScheduleAsync();

        var overview = await OverviewAsync();

        Assert.Equal(10 + CollectorSchedule().Length, overview.Jobs.Count);
        var source = Assert.Single(overview.ScheduleSources);
        Assert.Equal(Collector, source.Producer);
        Assert.Equal(CollectorSchedule().Length, source.EntryCount);

        var ccu = overview.Jobs.Single(j => j.JobName == "ccu");
        Assert.Equal(new DateTime(2026, 10, 6, 16, 0, 0), ccu.NextRunAt);

        var financial = overview.Jobs.Single(j => j.JobName == "financial");
        Assert.False(financial.Enabled);
        Assert.Null(financial.NextRunAt);
        Assert.Equal("paused on the Jobs page", financial.NextRunNote);
        Assert.Equal("0 4 * * *", financial.CronExpression);

        var steamdb = overview.Jobs.Single(j => j.JobName == "steamdb");
        Assert.Equal(3600, steamdb.TimeoutSeconds);
        Assert.Equal(4200, steamdb.StaleAfterSeconds);
    }

    [Fact]
    public async Task Running_section_drops_lost_rows()
    {
        await SeedScheduleAsync();
        await SeedAsync(
            // live: started 20 min ago, limit 1h10m
            Run("steamdb", JobRunStatus.Running, Now.AddMinutes(-20)),
            // killed by timeout: 3h ago, never finished
            Run("steamdb_followers", JobRunStatus.Running, Now.AddHours(-3)),
            // orphaned by an API restart (prod has 40 of these)
            Run(BackendJobNames.SteamPriceWatcher, JobRunStatus.Running, ApiStart.AddDays(-47), producer: JobRunProducer.CatoBackend),
            // current backend cycle
            Run(BackendJobNames.SteamPicsWatcher, JobRunStatus.Running, Now.AddMinutes(-1), producer: JobRunProducer.CatoBackend));

        var overview = await OverviewAsync();

        Assert.Equal(
            ["SteamPicsWatcher", "steamdb"],
            overview.Running.Select(r => r.JobName).OrderBy(n => n, StringComparer.Ordinal));

        var followers = overview.Jobs.Single(j => j.JobName == "steamdb_followers");
        Assert.Equal(JobRunStatus.Lost, followers.LastRun!.EffectiveStatus);
        Assert.Equal("no finish reported within 2h 10m", followers.LastRun.LostReason);

        var price = overview.Jobs.Single(j => j.JobName == BackendJobNames.SteamPriceWatcher);
        Assert.Equal(JobRunStatusEvaluator.ReasonWorkerRestarted, price.LastRun!.LostReason);
    }

    [Fact]
    public async Task Interval_job_next_run_follows_the_last_end()
    {
        await SeedAsync(Run(
            BackendJobNames.GameEnrichmentWatcher, JobRunStatus.PartialSuccess,
            Now.AddMinutes(-40), Now.AddMinutes(-10), JobRunProducer.CatoBackend));

        var overview = await OverviewAsync();

        var enrichment = overview.Jobs.Single(j => j.JobName == BackendJobNames.GameEnrichmentWatcher);
        Assert.Equal(JobScheduleKind.Interval, enrichment.ScheduleKind);
        Assert.Equal(60, enrichment.IntervalMinutes);
        Assert.Equal(Now.AddMinutes(50), enrichment.NextRunAt);
    }

    [Fact]
    public async Task Skipped_run_does_not_hide_the_last_real_run()
    {
        await SeedScheduleAsync();
        await SeedAsync(
            Run("steamdb_followers_priority", JobRunStatus.Succeeded, Now.AddMinutes(-66), Now.AddMinutes(-65)),
            Run("steamdb_followers_priority", JobRunStatus.Skipped, Now.AddMinutes(-6), Now.AddMinutes(-6)));

        var overview = await OverviewAsync();

        var job = overview.Jobs.Single(j => j.JobName == "steamdb_followers_priority");
        Assert.Equal(JobRunStatus.Succeeded, job.LastRun!.Status);
        Assert.Equal(2, job.RecentRuns.Count);
        Assert.Equal(JobRunStatus.Skipped, job.RecentRuns[0].Status);
    }

    [Fact]
    public async Task Job_without_a_schedule_still_shows_when_it_ran_recently()
    {
        await SeedAsync(Run("manual_backfill", JobRunStatus.Succeeded, Now.AddDays(-2), Now.AddDays(-2).AddMinutes(5)));

        var overview = await OverviewAsync();

        var job = overview.Jobs.Single(j => j.JobName == "manual_backfill");
        Assert.Equal(JobScheduleKind.None, job.ScheduleKind);
        Assert.Null(job.NextRunAt);
    }

    [Fact]
    public async Task Missing_web_api_key_disables_the_key_based_watchers()
    {
        await using var db = NewContext();

        var jobs = NewCatalog(db, webApiKey: "").BackendJobs().ToList();

        var profile = jobs.Single(j => j.JobName == BackendJobNames.SteamPlayerProfileWatcher);
        Assert.False(profile.Enabled);
        Assert.Equal("no Steam Web API key configured", profile.DisabledReason);
        Assert.True(jobs.Single(j => j.JobName == BackendJobNames.SteamPriceWatcher).Enabled);
    }

    [Fact]
    public async Task List_endpoint_marks_dead_running_rows_as_lost()
    {
        await SeedScheduleAsync();
        var older = Run("ccu", JobRunStatus.Running, Now.AddHours(-8));
        var newer = Run("ccu", JobRunStatus.Running, Now.AddMinutes(-2));
        await SeedAsync(older, newer);

        await using var db = NewContext();
        var handler = new GetJobRunsHandler(db, NewCatalog(db), new JobRunStatusResolver(db, WorkerStart(db), Clock));
        var runs = await handler.Handle(new GetJobRunsQuery("ccu", JobRunStatus.Running), CancellationToken.None);

        Assert.Equal(JobRunStatus.Running, runs.Single(r => r.Id == newer.Id).EffectiveStatus);
        var lost = runs.Single(r => r.Id == older.Id);
        Assert.Equal(JobRunStatus.Lost, lost.EffectiveStatus);
        Assert.Equal(JobRunStatusEvaluator.ReasonNewerRun, lost.LostReason);
    }
}
