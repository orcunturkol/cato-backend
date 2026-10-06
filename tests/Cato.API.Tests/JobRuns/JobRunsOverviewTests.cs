using Cato.API.Models.JobRuns;
using Cato.API.Services.Handlers.JobRuns;
using Cato.API.Services.JobRuns;
using Cato.API.Validators.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Jobs;

namespace Cato.API.Tests.JobRuns;

public class JobRunsOverviewTests : JobRunsDbTestBase
{
    private const string Collector = JobRunProducer.ExternalCollector;

    private static readonly List<JobScheduleEntryDto> CollectorSchedule =
    [
        new("ccu", "CCU", "0 */4 * * *", true, null, null),
        new("steamdb", "SteamDB rankings", "30 3 * * *", true, 3600, null),
        new("financial", "Partner financials", "0 4 * * *", false, null, "commented out in crontab"),
        new("steamdb_followers", "SteamDB followers", "0 6 * * *", true, 7200, null),
        new("steamdb_followers_priority", "SteamDB followers, priority", "15 * * * *", true, 7200, null),
    ];

    private async Task PublishAsync(List<JobScheduleEntryDto> entries)
    {
        await using var db = NewContext();
        await new PublishJobScheduleHandler(db, Clock).Handle(
            new PublishJobScheduleCommand(Collector, "crontab", "catoptric", entries), CancellationToken.None);
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

    private async Task<JobRunsOverviewDto> OverviewAsync()
    {
        await using var db = NewContext();
        var catalog = NewCatalog(db);
        var handler = new GetJobRunsOverviewHandler(
            db, catalog, new JobRunStatusResolver(db, Process, Clock), Process, Clock);
        return await handler.Handle(new GetJobRunsOverviewQuery(), CancellationToken.None);
    }

    [Fact]
    public async Task Overview_lists_backend_and_published_collector_jobs()
    {
        await PublishAsync(CollectorSchedule);

        var overview = await OverviewAsync();

        Assert.Equal(8 + CollectorSchedule.Count, overview.Jobs.Count);
        var source = Assert.Single(overview.ScheduleSources);
        Assert.Equal("catoptric", source.SourceHost);
        Assert.Equal(CollectorSchedule.Count, source.EntryCount);

        var ccu = overview.Jobs.Single(j => j.JobName == "ccu");
        Assert.Equal(new DateTime(2026, 10, 6, 16, 0, 0), ccu.NextRunAt);

        var financial = overview.Jobs.Single(j => j.JobName == "financial");
        Assert.False(financial.Enabled);
        Assert.Null(financial.NextRunAt);
        Assert.Equal("commented out in crontab", financial.NextRunNote);
        Assert.Equal("0 4 * * *", financial.CronExpression);

        var steamdb = overview.Jobs.Single(j => j.JobName == "steamdb");
        Assert.Equal(3600, steamdb.TimeoutSeconds);
        Assert.Equal(4200, steamdb.StaleAfterSeconds);
    }

    [Fact]
    public async Task Running_section_drops_lost_rows()
    {
        await PublishAsync(CollectorSchedule);
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
        Assert.Equal(JobRunStatusEvaluator.ReasonApiRestarted, price.LastRun!.LostReason);
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
        await PublishAsync(CollectorSchedule);
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
    public async Task Publishing_again_replaces_the_schedule()
    {
        await PublishAsync(CollectorSchedule);
        await PublishAsync([new("ccu", "CCU", "0 */2 * * *", true, null, null)]);

        var overview = await OverviewAsync();

        Assert.Equal(1, overview.ScheduleSources.Single().EntryCount);
        Assert.DoesNotContain(overview.Jobs, j => j.JobName == "steamdb");
        Assert.Equal("0 */2 * * *", overview.Jobs.Single(j => j.JobName == "ccu").CronExpression);
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
        await PublishAsync(CollectorSchedule);
        var older = Run("ccu", JobRunStatus.Running, Now.AddHours(-8));
        var newer = Run("ccu", JobRunStatus.Running, Now.AddMinutes(-2));
        await SeedAsync(older, newer);

        await using var db = NewContext();
        var handler = new GetJobRunsHandler(db, NewCatalog(db), new JobRunStatusResolver(db, Process, Clock));
        var runs = await handler.Handle(new GetJobRunsQuery("ccu", JobRunStatus.Running), CancellationToken.None);

        Assert.Equal(JobRunStatus.Running, runs.Single(r => r.Id == newer.Id).EffectiveStatus);
        var lost = runs.Single(r => r.Id == older.Id);
        Assert.Equal(JobRunStatus.Lost, lost.EffectiveStatus);
        Assert.Equal(JobRunStatusEvaluator.ReasonNewerRun, lost.LostReason);
    }

    [Theory]
    [InlineData("external-collector", "0 3 * * *", true, null, true)]
    [InlineData("cato-backend", "0 3 * * *", true, null, false)]
    [InlineData("external-collector", "every day", true, null, false)]
    [InlineData("external-collector", null, true, null, false)]
    [InlineData("external-collector", null, false, null, true)]
    [InlineData("external-collector", "0 3 * * *", true, 0, false)]
    public void Schedule_validator(string producer, string? cron, bool enabled, int? timeout, bool valid)
    {
        var command = new PublishJobScheduleCommand(
            producer, "crontab", null, [new JobScheduleEntryDto("ccu", null, cron, enabled, timeout, null)]);

        Assert.Equal(valid, new PublishJobScheduleValidator().Validate(command).IsValid);
    }
}
