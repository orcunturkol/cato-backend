using System.Text.Json;
using Cato.API.Models.ScheduledJobs;
using Cato.API.Services.Handlers.ScheduledJobs;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Jobs;

namespace Cato.API.Tests.JobRuns;

public class ScheduledJobsTests : JobRunsDbTestBase
{
    private void Seed(params ScheduledJob[] jobs)
    {
        using var db = NewContext();
        db.ScheduledJobs.AddRange(jobs);
        db.SaveChanges();
    }

    private static ScheduledJob Job(string name, string managedBy, string? cron = "0 11 * * *",
        int timeoutSeconds = 2700, string group = JobAgentGroups.Steamworks) => new()
    {
        Name = name, Label = name, AgentGroup = group, Module = $"orchestrators.run_{name}",
        Cron = cron, Enabled = true, TimeoutSeconds = timeoutSeconds, ManagedBy = managedBy,
        UpdatedAt = Now.AddDays(-1), UpdatedBy = "seed",
    };

    private static UpdateScheduledJobRequest Edit(string? cron, int timeoutSeconds = 2700, bool enabled = true) =>
        new(cron, enabled, timeoutSeconds, 0.10m, 1, "move after the store refresh", "orcun");

    [Fact]
    public async Task A_job_still_on_the_crontab_cannot_be_edited()
    {
        Seed(Job("steamworks_traffic", ScheduledJobManagedBy.Cron));
        await using var db = NewContext();

        var result = await new UpdateScheduledJobHandler(db, Clock)
            .Handle(new UpdateScheduledJobCommand("steamworks_traffic", Edit("0 14 * * *")), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("host crontab", result.ErrorMessage);
        Assert.Empty(db.ScheduledJobChanges);
    }

    [Fact]
    public async Task An_edit_saves_the_job_and_logs_old_and_new_values()
    {
        Seed(Job("steamworks_traffic", ScheduledJobManagedBy.Dispatcher),
             Job("steamworks_navstats", ScheduledJobManagedBy.Dispatcher, "30 14 * * *", 900));
        await using (var db = NewContext())
        {
            var result = await new UpdateScheduledJobHandler(db, Clock)
                .Handle(new UpdateScheduledJobCommand("steamworks_traffic", Edit(" 0 14 * * * ")), default);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal("0 14 * * *", result.Data!.Job.Cron);
            Assert.True(result.Data.Job.Editable);
            var warning = Assert.Single(result.Data.Warnings);
            Assert.Contains("steamworks_navstats", warning);
        }

        await using var check = NewContext();
        var saved = check.ScheduledJobs.Single(j => j.Name == "steamworks_traffic");
        Assert.Equal("0 14 * * *", saved.Cron);
        Assert.Equal("orcun", saved.UpdatedBy);
        var change = Assert.Single(check.ScheduledJobChanges);
        Assert.Equal("move after the store refresh", change.Reason);
        Assert.Equal("0 11 * * *", JsonDocument.Parse(change.OldJson).RootElement.GetProperty("cron").GetString());
        Assert.Equal("0 14 * * *", JsonDocument.Parse(change.NewJson).RootElement.GetProperty("cron").GetString());
    }

    [Fact]
    public async Task An_invalid_edit_changes_nothing()
    {
        Seed(Job("ccu", ScheduledJobManagedBy.Dispatcher, "0 */4 * * *", 3600, JobAgentGroups.Light));
        await using var db = NewContext();

        var result = await new UpdateScheduledJobHandler(db, Clock)
            .Handle(new UpdateScheduledJobCommand("ccu", Edit("*/30 * * * *", 3600)), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("longer than the time limit", result.ErrorMessage);
        Assert.Empty(db.ScheduledJobChanges);
        Assert.Equal("0 */4 * * *", db.ScheduledJobs.Single().Cron);
    }

    [Fact]
    public async Task An_unknown_job_is_not_found()
    {
        await using var db = NewContext();

        var result = await new UpdateScheduledJobHandler(db, Clock)
            .Handle(new UpdateScheduledJobCommand("nope", Edit("0 3 * * *")), default);

        Assert.Equal(UpdateScheduledJobHandler.NotFound, result.ErrorMessage);
    }

    [Fact]
    public async Task The_preview_shows_errors_without_saving()
    {
        Seed(Job("ccu", ScheduledJobManagedBy.Cron, "0 */4 * * *", 3600, JobAgentGroups.Light));
        await using var db = NewContext();

        var result = await new PreviewScheduleHandler(db, Clock).Handle(
            new PreviewScheduleQuery("ccu", new PreviewScheduleRequest("0 */2 * * *", true, 3600, 0.10m, 1)), default);

        Assert.True(result.Data!.Valid);
        Assert.Equal(5, result.Data.NextRuns.Count);
        Assert.Equal("0 */4 * * *", db.ScheduledJobs.Single().Cron);
    }

    [Fact]
    public async Task The_list_marks_which_jobs_are_editable()
    {
        Seed(Job("ccu", ScheduledJobManagedBy.Cron, "0 */4 * * *", 3600, JobAgentGroups.Light),
             Job("steamworks_traffic", ScheduledJobManagedBy.Dispatcher));
        await using var db = NewContext();

        var list = await new GetScheduledJobsHandler(db, Clock).Handle(new GetScheduledJobsQuery(), default);

        Assert.Equal(["ccu", "steamworks_traffic"], list.Select(j => j.Name));
        Assert.Equal([false, true], list.Select(j => j.Editable));
        Assert.All(list, j => Assert.Equal(5, j.NextRuns.Count));
    }

    [Fact]
    public async Task The_catalog_takes_every_collector_job_from_the_table()
    {
        await using (var db = NewContext())
        {
            db.ScheduledJobs.Add(Job("steamworks_traffic", ScheduledJobManagedBy.Dispatcher, "0 14 * * *"));
            db.ScheduledJobs.Add(Job("ccu", ScheduledJobManagedBy.Cron, "0 */2 * * *", 3600, JobAgentGroups.Light));
            db.ScheduledJobs.Add(Job(BackendJobNames.SteamPriceWatcher, ScheduledJobManagedBy.Worker, null, 0, JobAgentGroups.Worker));
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var catalog = await NewCatalog(read).LoadAsync(default);

        Assert.Equal(["0 14 * * *"], catalog.Find(JobRunProducer.ExternalCollector, "steamworks_traffic")!.CronExpressions);
        Assert.Equal(["0 */2 * * *"], catalog.Find(JobRunProducer.ExternalCollector, "ccu")!.CronExpressions);
        Assert.Null(catalog.Find(JobRunProducer.ExternalCollector, BackendJobNames.SteamPriceWatcher));
        Assert.Contains(catalog.Sources, s => s.Source == "dispatcher" && s.EntryCount == 2);
    }

    [Fact]
    public void A_job_with_no_cron_runs_on_request_only()
    {
        var job = JobCatalog.FromScheduledJob(Job("steamdb_followers_priority", ScheduledJobManagedBy.Dispatcher, null, 1200));

        Assert.Equal(JobScheduleKind.None, job.ScheduleKind);
        Assert.Empty(job.CronExpressions);
        Assert.Equal(TimeSpan.FromSeconds(1200) + JobRunRules.TimeoutGrace, job.StaleAfter);
    }
}
