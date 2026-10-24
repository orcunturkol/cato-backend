using Cato.API.Services.Dispatch;
using Cato.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.JobRuns;

public class JobDispatchServiceTests : JobRunsDbTestBase
{
    // Base clock: 2026-10-06 15:21 UTC.

    private JobDispatchService Service(Cato.Infrastructure.Database.CatoDbContext db) =>
        new(db, Options.Create(new JobDispatcherSettings()));

    private void Seed(params ScheduledJob[] jobs)
    {
        using var db = NewContext();
        db.ScheduledJobs.AddRange(jobs);
        db.SaveChanges();
    }

    private static ScheduledJob Job(string name, string cron, string group = JobAgentGroups.Light,
        string managedBy = ScheduledJobManagedBy.Dispatcher, DateTime? updatedAt = null, int timeoutSeconds = 600) => new()
    {
        Name = name, Label = name, AgentGroup = group, Module = $"orchestrators.run_{name}", Args = "--max-games 5",
        Cron = cron, Enabled = true, TimeoutSeconds = timeoutSeconds, ManagedBy = managedBy,
        UpdatedAt = updatedAt ?? Now.AddHours(-2),
    };

    [Fact]
    public async Task Only_the_newest_due_slot_is_dispatched_and_never_twice()
    {
        Seed(Job("ccu", "*/10 * * * *"));
        await using var db = NewContext();
        var service = Service(db);

        Assert.Equal(1, await service.DispatchDueAsync(Now, default));
        Assert.Equal(0, await service.DispatchDueAsync(Now.AddSeconds(20), default));

        var dispatch = Assert.Single(db.JobDispatches);
        Assert.Equal(new DateTime(2026, 10, 6, 15, 20, 0), dispatch.ScheduledAt);
        Assert.Equal(JobDispatchStatus.Pending, dispatch.Status);
    }

    [Fact]
    public async Task A_slot_cron_already_ran_before_the_switch_is_not_repeated()
    {
        Seed(Job("financial", "20 15 * * *", updatedAt: Now.AddMinutes(-1)));
        await using var db = NewContext();

        Assert.Equal(0, await Service(db).DispatchDueAsync(Now, default));
    }

    [Fact]
    public async Task Jobs_still_on_cron_and_paused_jobs_are_left_alone()
    {
        var paused = Job("special_events", "*/10 * * * *");
        paused.Enabled = false;
        Seed(Job("ccu", "*/10 * * * *", managedBy: ScheduledJobManagedBy.Cron), paused);
        await using var db = NewContext();

        Assert.Equal(0, await Service(db).DispatchDueAsync(Now, default));
    }

    [Fact]
    public async Task After_downtime_only_the_newest_missed_slot_runs()
    {
        Seed(Job("group_member_count", "0 3 * * *", updatedAt: Now.AddDays(-5)));
        await using var db = NewContext();

        Assert.Equal(1, await Service(db).DispatchDueAsync(Now, default));
        Assert.Equal(new DateTime(2026, 10, 6, 3, 0, 0), db.JobDispatches.Single().ScheduledAt);
    }

    [Fact]
    public async Task A_slot_whose_previous_run_is_still_active_is_skipped_and_shown_as_a_run()
    {
        Seed(Job("ccu", "*/10 * * * *"));
        await using var db = NewContext();
        var service = Service(db);

        await service.DispatchDueAsync(Now, default);
        await service.DispatchDueAsync(Now.AddMinutes(10), default);

        var skipped = db.JobDispatches.Single(d => d.Status == JobDispatchStatus.Skipped);
        Assert.Equal(new DateTime(2026, 10, 6, 15, 30, 0), skipped.ScheduledAt);
        var run = Assert.Single(db.JobRuns);
        Assert.Equal(JobRunStatus.Skipped, run.Status);
        Assert.Contains("previous run is still active", run.ErrorMessage);
    }

    [Fact]
    public async Task A_group_runs_one_job_at_a_time_and_claims_the_oldest_first()
    {
        Seed(Job("steamdb", "20 15 * * *", JobAgentGroups.SteamDb),
             Job("steamdb_followers", "15 15 * * *", JobAgentGroups.SteamDb),
             Job("ccu", "*/10 * * * *"));
        await using var db = NewContext();
        var service = Service(db);
        await service.DispatchDueAsync(Now, default);

        var first = await service.ClaimAsync(JobAgentGroups.SteamDb, "steamdb@a", "a", Now, default);
        var second = await service.ClaimAsync(JobAgentGroups.SteamDb, "steamdb@b", "b", Now, default);
        var light = await service.ClaimAsync(JobAgentGroups.Light, "light@a", "a", Now, default);

        Assert.Equal("steamdb_followers", first!.JobName);
        Assert.Equal("orchestrators.run_steamdb_followers", first.Module);
        Assert.Equal("--max-games 5", first.Args);
        Assert.Equal(600, first.TimeoutSeconds);
        Assert.Null(second);
        Assert.Equal("ccu", light!.JobName);

        Assert.Equal(FinishOutcome.Recorded,
            await service.FinishAsync(first.DispatchId, "steamdb@a", 0, null, Now.AddMinutes(5), default));
        var next = await service.ClaimAsync(JobAgentGroups.SteamDb, "steamdb@b", "b", Now.AddMinutes(5), default);
        Assert.Equal("steamdb", next!.JobName);

        var agents = db.JobAgents.OrderBy(a => a.AgentId).ToList();
        Assert.Equal(["light@a", "steamdb@a", "steamdb@b"], agents.Select(a => a.AgentId));
        Assert.Null(agents.Single(a => a.AgentId == "steamdb@a").CurrentDispatchId);
        Assert.Equal(next.DispatchId, agents.Single(a => a.AgentId == "steamdb@b").CurrentDispatchId);
    }

    [Fact]
    public async Task Only_the_claiming_agent_can_finish_a_run()
    {
        Seed(Job("ccu", "*/10 * * * *"));
        await using var db = NewContext();
        var service = Service(db);
        await service.DispatchDueAsync(Now, default);
        var run = await service.ClaimAsync(JobAgentGroups.Light, "light@a", "a", Now, default);

        Assert.Equal(FinishOutcome.NotYourClaim, await service.FinishAsync(run!.DispatchId, "light@b", 0, null, Now, default));
        Assert.Equal(FinishOutcome.NotFound, await service.FinishAsync(Guid.NewGuid(), "light@a", 0, null, Now, default));
        Assert.Equal(FinishOutcome.Recorded, await service.FinishAsync(run.DispatchId, "light@a", 124, "time limit reached", Now, default));

        var done = db.JobDispatches.Single();
        Assert.Equal(JobDispatchStatus.Finished, done.Status);
        Assert.Equal(124, done.ExitCode);
    }

    [Fact]
    public async Task A_time_limit_stop_fails_the_run_and_an_agent_stop_does_not()
    {
        Seed(Job("ccu", "*/10 * * * *"), Job("financial", "*/10 * * * *"),
            Job("group_member_count", "*/10 * * * *"), Job("sale_collection", "*/10 * * * *"));
        await using var db = NewContext();
        var service = Service(db);
        await service.DispatchDueAsync(Now, default);

        // One claim at a time; the job reports its run, then the agent reports the finish.
        async Task<Guid> RunAsync(string reported, int exitCode, string note, int minutes)
        {
            var claimed = await service.ClaimAsync(JobAgentGroups.Light, "light@a", "a", Now, default);
            db.JobRuns.Add(new JobRun
            {
                Id = Guid.NewGuid(), JobName = claimed!.JobName, Producer = JobRunProducer.ExternalCollector,
                StartTime = Now, EndTime = reported == JobRunStatus.Running ? null : Now.AddMinutes(minutes),
                Status = reported, DispatchId = claimed.DispatchId,
            });
            await db.SaveChangesAsync();
            await service.FinishAsync(claimed.DispatchId, "light@a", exitCode, note, Now.AddMinutes(minutes), default);
            return claimed.DispatchId;
        }

        var timedOut = await RunAsync(JobRunStatus.Interrupted, 124, JobDispatchService.TimeLimitNote, 30);
        var neverReported = await RunAsync(JobRunStatus.Running, 137, JobDispatchService.TimeLimitNote, 32);
        var agentStopped = await RunAsync(JobRunStatus.Interrupted, 143, "agent stopped", 5);
        var reportedFirst = await RunAsync(JobRunStatus.Succeeded, 124, JobDispatchService.TimeLimitNote, 30);

        await using var check = NewContext();
        var runs = check.JobRuns.ToDictionary(r => r.DispatchId!.Value);
        Assert.Equal(JobRunStatus.Failed, runs[timedOut].Status);
        Assert.Equal("time limit reached: the agent stopped the run after 30 min (exit 124)", runs[timedOut].ErrorMessage);
        Assert.Equal(JobRunStatus.Failed, runs[neverReported].Status);
        Assert.Equal(Now.AddMinutes(32), runs[neverReported].EndTime);
        Assert.Equal((long)TimeSpan.FromMinutes(32).TotalMilliseconds, runs[neverReported].DurationMs);
        Assert.Equal(JobRunStatus.Interrupted, runs[agentStopped].Status);
        Assert.Null(runs[agentStopped].ErrorMessage);
        Assert.Equal(JobRunStatus.Succeeded, runs[reportedFirst].Status);
    }

    [Fact]
    public async Task An_agent_stop_queues_the_run_once_more_but_never_a_second_time()
    {
        Seed(Job("ccu", "*/10 * * * *"));
        await using var db = NewContext();
        var service = Service(db);
        await service.DispatchDueAsync(Now, default);

        async Task<ClaimedRun> StopAsync(string agentId, DateTime at)
        {
            var claimed = await service.ClaimAsync(JobAgentGroups.Light, agentId, "a", at, default);
            db.JobRuns.Add(new JobRun
            {
                Id = Guid.NewGuid(), JobName = claimed!.JobName, Producer = JobRunProducer.ExternalCollector,
                StartTime = at, Status = JobRunStatus.Interrupted, DispatchId = claimed.DispatchId,
            });
            await db.SaveChangesAsync();
            await service.FinishAsync(claimed.DispatchId, agentId, 143, JobDispatchService.AgentStoppedNote, at.AddMinutes(1), default);
            return claimed;
        }

        var first = await StopAsync("light@a", Now);
        Assert.Equal(JobDispatchKind.Scheduled, first.Kind);

        // The next agent task claims the retry; when it is stopped too, nothing is queued.
        var retry = await StopAsync("light@b", Now.AddMinutes(2));
        Assert.Equal(JobDispatchKind.Retry, retry.Kind);
        Assert.Equal("ccu", retry.JobName);
        Assert.Null(await service.ClaimAsync(JobAgentGroups.Light, "light@c", "c", Now.AddMinutes(4), default));

        await using var check = NewContext();
        Assert.Equal(2, check.JobDispatches.Count());
    }

    [Fact]
    public async Task An_agent_stop_is_not_retried_after_a_reported_result_or_for_a_paused_job()
    {
        Seed(Job("ccu", "*/10 * * * *"), Job("financial", "*/10 * * * *"));
        await using var db = NewContext();
        var service = Service(db);
        await service.DispatchDueAsync(Now, default);

        // The run reported Succeeded, then the agent stopped while the process still ran.
        var reported = await service.ClaimAsync(JobAgentGroups.Light, "light@a", "a", Now, default);
        db.JobRuns.Add(new JobRun
        {
            Id = Guid.NewGuid(), JobName = reported!.JobName, Producer = JobRunProducer.ExternalCollector,
            StartTime = Now, EndTime = Now.AddMinutes(1), Status = JobRunStatus.Succeeded, DispatchId = reported.DispatchId,
        });
        await db.SaveChangesAsync();
        await service.FinishAsync(reported.DispatchId, "light@a", 143, JobDispatchService.AgentStoppedNote, Now.AddMinutes(1), default);

        // The job was paused while it ran (no run row: stopped before it reported).
        var paused = await service.ClaimAsync(JobAgentGroups.Light, "light@a", "a", Now.AddMinutes(2), default);
        var job = db.ScheduledJobs.Single(j => j.Name == paused!.JobName);
        job.Enabled = false;
        await db.SaveChangesAsync();
        await service.FinishAsync(paused!.DispatchId, "light@a", 143, JobDispatchService.AgentStoppedNote, Now.AddMinutes(3), default);

        Assert.Null(await service.ClaimAsync(JobAgentGroups.Light, "light@b", "b", Now.AddMinutes(4), default));
        await using var check = NewContext();
        Assert.Equal(2, check.JobDispatches.Count());
    }

    [Fact]
    public async Task Unclaimed_runs_expire_and_silent_claims_are_lost()
    {
        Seed(Job("ccu", "*/10 * * * *"), Job("steamdb", "0 3 * * *", JobAgentGroups.SteamDb, timeoutSeconds: 600));
        await using (var db = NewContext())
        {
            db.JobDispatches.AddRange(
                new JobDispatch { Id = Guid.NewGuid(), JobName = "ccu", AgentGroup = JobAgentGroups.Light,
                    ScheduledAt = Now.AddHours(-7), Status = JobDispatchStatus.Pending, CreatedAt = Now.AddHours(-7) },
                new JobDispatch { Id = Guid.NewGuid(), JobName = "steamdb", AgentGroup = JobAgentGroups.SteamDb,
                    ScheduledAt = Now.AddHours(-1), Status = JobDispatchStatus.Claimed, ClaimedBy = "steamdb@a",
                    ClaimedAt = Now.AddMinutes(-21), CreatedAt = Now.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        await using var check = NewContext();
        Assert.Equal(2, await Service(check).HousekeepAsync(Now, default));
        Assert.Equal(JobDispatchStatus.Expired, check.JobDispatches.Single(d => d.JobName == "ccu").Status);
        Assert.Equal(JobDispatchStatus.Lost, check.JobDispatches.Single(d => d.JobName == "steamdb").Status);
    }

    [Fact]
    public async Task Run_now_needs_the_dispatcher_and_no_active_run()
    {
        Seed(Job("ccu", "0 */4 * * *", managedBy: ScheduledJobManagedBy.Cron),
             Job("steamdb_followers_priority", "0 0 1 1 *", JobAgentGroups.SteamDb));
        await using var db = NewContext();
        var service = Service(db);

        Assert.Equal(RunRequestOutcome.NotDispatcherManaged, (await service.RequestRunAsync("ccu", "me", Now, default)).Outcome);
        Assert.Equal(RunRequestOutcome.NotFound, (await service.RequestRunAsync("nope", "me", Now, default)).Outcome);
        var queued = await service.RequestRunAsync("steamdb_followers_priority", "follower history: app 1", Now, default);
        Assert.Equal(RunRequestOutcome.Queued, queued.Outcome);
        var again = await service.RequestRunAsync("steamdb_followers_priority", "follower history: app 2", Now.AddSeconds(1), default);
        Assert.Equal(RunRequestOutcome.AlreadyActive, again.Outcome);
        Assert.Equal(queued.DispatchId, again.DispatchId);

        var manual = db.JobDispatches.Single();
        Assert.Equal(JobDispatchKind.Manual, manual.Kind);
        Assert.Equal("follower history: app 1", manual.RequestedBy);
    }

    [Fact]
    public async Task A_group_switch_logs_each_job_and_going_back_to_cron_expires_waiting_runs()
    {
        Seed(Job("steamworks_wishlist", "0 10 * * *", JobAgentGroups.Steamworks, ScheduledJobManagedBy.Cron),
             Job("steamworks_traffic", "0 11 * * *", JobAgentGroups.Steamworks, ScheduledJobManagedBy.Cron),
             Job("ccu", "0 */4 * * *", managedBy: ScheduledJobManagedBy.Cron));
        await using var db = NewContext();
        var service = Service(db);

        var moved = await service.SetGroupManagedByAsync(JobAgentGroups.Steamworks, ScheduledJobManagedBy.Dispatcher, "orcun", "cutover D1", Now, default);
        Assert.Equal(["steamworks_traffic", "steamworks_wishlist"], moved.Order());
        Assert.Equal(2, db.ScheduledJobChanges.Count());
        Assert.Equal(ScheduledJobManagedBy.Cron, db.ScheduledJobs.Single(j => j.Name == "ccu").ManagedBy);

        await service.RequestRunAsync("steamworks_traffic", "me", Now, default);
        await service.SetGroupManagedByAsync(JobAgentGroups.Steamworks, ScheduledJobManagedBy.Cron, "orcun", "rollback", Now.AddMinutes(1), default);
        Assert.Equal(JobDispatchStatus.Expired, db.JobDispatches.Single().Status);
    }

    [Fact]
    public async Task An_agent_that_claims_again_closes_the_run_it_lost()
    {
        Seed(Job("ccu", "*/10 * * * *"), Job("financial", "15 15 * * *"));
        await using var db = NewContext();
        var service = Service(db);
        await service.DispatchDueAsync(Now, default);

        var first = await service.ClaimAsync(JobAgentGroups.Light, "light@a", "a", Now, default);
        // The agent was killed during the run and restarted: no finish came.
        var second = await service.ClaimAsync(JobAgentGroups.Light, "light@a", "a", Now.AddMinutes(1), default);

        Assert.NotNull(second);
        Assert.NotEqual(first!.DispatchId, second.DispatchId);
        await using var check = NewContext();
        var lost = check.JobDispatches.Single(d => d.Id == first.DispatchId);
        Assert.Equal(JobDispatchStatus.Lost, lost.Status);
        Assert.Contains("claimed again", lost.Note);
    }

    [Fact]
    public async Task A_late_heartbeat_for_a_finished_run_does_not_show_it_as_current()
    {
        Seed(Job("ccu", "*/10 * * * *"));
        await using var db = NewContext();
        var service = Service(db);
        await service.DispatchDueAsync(Now, default);
        var run = await service.ClaimAsync(JobAgentGroups.Light, "light@a", "a", Now, default);
        await service.HeartbeatAsync(JobAgentGroups.Light, "light@a", "a", run!.DispatchId, Now, default);
        Assert.Equal(run.DispatchId, db.JobAgents.Single().CurrentDispatchId);

        await service.FinishAsync(run.DispatchId, "light@a", 0, null, Now.AddMinutes(1), default);
        await service.HeartbeatAsync(JobAgentGroups.Light, "light@a", "a", run.DispatchId, Now.AddMinutes(1), default);

        await using var check = NewContext();
        Assert.Null(check.JobAgents.Single().CurrentDispatchId);
    }

    [Fact]
    public void Newest_slot_reads_several_expressions()
    {
        var slot = JobDispatchService.NewestSlot("0 10 * * * | 0 15 * * *", Now.AddDays(-1), Now);

        Assert.Equal(new DateTime(2026, 10, 6, 15, 0, 0), slot);
        Assert.Null(JobDispatchService.NewestSlot("0 10 * * *", Now, Now));
    }
}
