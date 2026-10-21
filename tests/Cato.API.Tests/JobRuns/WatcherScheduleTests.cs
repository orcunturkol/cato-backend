using Cato.API.Models.ScheduledJobs;
using Cato.API.Services.Dispatch;
using Cato.API.Services.Handlers.ScheduledJobs;
using Cato.Domain.Entities;
using Cato.Infrastructure.Jobs;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.JobRuns;

public class WatcherScheduleTests : JobRunsDbTestBase
{
    private static readonly WatcherDefaults News = new("Owned game news fetch", TimeSpan.FromHours(6), true);

    private async Task<WatcherScheduleState> ReadAsync(string job = BackendJobNames.GameNewsWatcher, WatcherDefaults? defaults = null)
    {
        await using var db = NewContext();
        return await WatcherSchedule.ReadAsync(db, job, defaults ?? News, Now, default);
    }

    private ScheduledJob Row(string job = BackendJobNames.GameNewsWatcher)
    {
        using var db = NewContext();
        return db.ScheduledJobs.Single(j => j.Name == job);
    }

    private static UpdateScheduledJobRequest Edit(int? intervalMinutes, bool enabled = true) =>
        new(Cron: "0 3 * * *", enabled, TimeoutSeconds: 600, 0.20m, 2, "every hour during the sale", "orcun", intervalMinutes);

    [Fact]
    public async Task The_first_read_writes_the_row_from_the_settings()
    {
        var state = await ReadAsync();

        Assert.Equal(new WatcherScheduleState(TimeSpan.FromHours(6), true), state);
        var row = Row();
        Assert.Equal((ScheduledJobManagedBy.Worker, JobAgentGroups.Worker, 360, (string?)null),
            (row.ManagedBy, row.AgentGroup, row.IntervalMinutes, row.Cron));
        Assert.Equal("Owned game news fetch", row.Label);
    }

    [Fact]
    public async Task After_that_the_row_wins_over_the_settings()
    {
        await ReadAsync();
        await using (var db = NewContext())
        {
            var row = db.ScheduledJobs.Single();
            row.IntervalMinutes = 60;
            row.Enabled = false;
            await db.SaveChangesAsync();
        }

        var state = await ReadAsync(defaults: News with { Interval = TimeSpan.FromMinutes(5) });

        Assert.Equal(new WatcherScheduleState(TimeSpan.FromHours(1), false), state);
    }

    [Fact]
    public void A_watcher_runs_when_enabled_and_its_interval_has_passed()
    {
        var on = new WatcherScheduleState(TimeSpan.FromHours(1), true);

        Assert.Null(WatcherSchedule.TimeUntilDue(on, previousEndUtc: null, Now));
        Assert.Null(WatcherSchedule.TimeUntilDue(on, Now.AddHours(-1), Now));
        Assert.Equal(TimeSpan.FromMinutes(20), WatcherSchedule.TimeUntilDue(on, Now.AddMinutes(-40), Now));
        Assert.Equal(WatcherSchedule.PollInterval,
            WatcherSchedule.TimeUntilDue(on with { Enabled = false }, previousEndUtc: null, Now));
    }

    [Fact]
    public async Task The_catalog_takes_interval_and_switch_from_the_row_but_a_missing_key_still_blocks()
    {
        await ReadAsync(BackendJobNames.GameNewsWatcher);
        await ReadAsync(BackendJobNames.PlayerAchievementWatcher, new("Player achievement fetch", TimeSpan.FromMinutes(10), true));
        await using (var db = NewContext())
        {
            db.ScheduledJobs.Single(j => j.Name == BackendJobNames.GameNewsWatcher).IntervalMinutes = 45;
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var withKey = await NewCatalog(read).LoadAsync(default);
        var withoutKey = await NewCatalog(read, webApiKey: "").LoadAsync(default);

        var news = withKey.Find(JobRunProducer.CatoBackend, BackendJobNames.GameNewsWatcher)!;
        Assert.Equal((45, true), (news.IntervalMinutes, news.Enabled));
        var player = withoutKey.Find(JobRunProducer.CatoBackend, BackendJobNames.PlayerAchievementWatcher)!;
        Assert.False(player.Enabled);
        Assert.Equal("no Steam Web API key configured", player.DisabledReason);
    }

    [Fact]
    public async Task A_paused_row_shows_as_paused_on_the_jobs_page()
    {
        await ReadAsync();
        await using (var db = NewContext())
        {
            db.ScheduledJobs.Single().Enabled = false;
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var news = (await NewCatalog(read).LoadAsync(default)).Find(JobRunProducer.CatoBackend, BackendJobNames.GameNewsWatcher)!;

        Assert.Equal((false, "paused on the Jobs page"), (news.Enabled, news.DisabledReason));
    }

    [Fact]
    public async Task A_watcher_edit_saves_the_interval_and_ignores_cron_and_time_limit()
    {
        await ReadAsync();
        await using var db = NewContext();

        var result = await new UpdateScheduledJobHandler(db, Clock)
            .Handle(new UpdateScheduledJobCommand(BackendJobNames.GameNewsWatcher, Edit(60)), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Data!.Job.Editable);
        Assert.Empty(result.Data.Warnings);
        var row = Row();
        Assert.Equal((60, (string?)null, 0, 0.20m), (row.IntervalMinutes, row.Cron, row.TimeoutSeconds, row.AlertFailedRatio));
        Assert.Contains("\"intervalMinutes\":60", db.ScheduledJobChanges.Single().NewJson);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(7 * 24 * 60 + 1)]
    public async Task A_watcher_needs_an_interval_from_one_minute_to_seven_days(int? minutes)
    {
        await ReadAsync();
        await using var db = NewContext();

        var result = await new UpdateScheduledJobHandler(db, Clock)
            .Handle(new UpdateScheduledJobCommand(BackendJobNames.GameNewsWatcher, Edit(minutes)), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("interval", result.ErrorMessage);
    }

    [Fact]
    public async Task The_dispatcher_and_run_now_leave_watcher_rows_alone()
    {
        await ReadAsync();
        await using var db = NewContext();
        var dispatch = new JobDispatchService(db, Options.Create(new JobDispatcherSettings()));

        Assert.Equal(0, await dispatch.DispatchDueAsync(Now, default));
        var run = await dispatch.RequestRunAsync(BackendJobNames.GameNewsWatcher, "orcun", Now, default);
        Assert.Equal(RunRequestOutcome.NotDispatcherManaged, run.Outcome);
    }
}
