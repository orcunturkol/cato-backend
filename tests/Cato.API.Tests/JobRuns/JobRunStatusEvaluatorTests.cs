using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;

namespace Cato.API.Tests.JobRuns;

public class JobRunStatusEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ApiStart = new(2026, 10, 6, 14, 17, 40, DateTimeKind.Utc);

    private static JobRun Run(
        string status,
        DateTime start,
        string producer = JobRunProducer.CatoBackend,
        string job = "PlayerAchievementWatcher") => new()
    {
        Id = Guid.NewGuid(),
        JobName = job,
        Producer = producer,
        Status = status,
        StartTime = start,
    };

    [Fact]
    public void Finished_status_is_kept()
    {
        var run = Run(JobRunStatus.Failed, Now.AddDays(-30));

        var (status, reason) = JobRunStatusEvaluator.Evaluate(run, true, TimeSpan.FromMinutes(1), Now, ApiStart);

        Assert.Equal(JobRunStatus.Failed, status);
        Assert.Null(reason);
    }

    [Fact]
    public void Running_row_with_a_newer_run_is_lost()
    {
        var run = Run(JobRunStatus.Running, Now.AddMinutes(-5));

        var (status, reason) = JobRunStatusEvaluator.Evaluate(run, true, TimeSpan.FromHours(1), Now, ApiStart);

        Assert.Equal(JobRunStatus.Lost, status);
        Assert.Equal(JobRunStatusEvaluator.ReasonNewerRun, reason);
    }

    [Fact]
    public void Backend_run_started_before_the_api_is_lost()
    {
        var run = Run(JobRunStatus.Running, ApiStart.AddMinutes(-1));

        var (status, reason) = JobRunStatusEvaluator.Evaluate(run, false, TimeSpan.FromHours(48), Now, ApiStart);

        Assert.Equal(JobRunStatus.Lost, status);
        Assert.Equal(JobRunStatusEvaluator.ReasonApiRestarted, reason);
    }

    [Fact]
    public void Collector_run_started_before_the_api_is_not_lost_for_that_reason()
    {
        var run = Run(JobRunStatus.Running, ApiStart.AddMinutes(-1), JobRunProducer.ExternalCollector, "steamdb");

        var (status, _) = JobRunStatusEvaluator.Evaluate(run, false, TimeSpan.FromHours(2), Now, ApiStart);

        Assert.Equal(JobRunStatus.Running, status);
    }

    [Fact]
    public void Running_row_past_its_limit_is_lost()
    {
        var limit = JobRunRules.ForTimeout(3600);
        var run = Run(JobRunStatus.Running, Now - limit - TimeSpan.FromSeconds(1), JobRunProducer.ExternalCollector, "steamdb");

        var (status, reason) = JobRunStatusEvaluator.Evaluate(run, false, limit, Now, ApiStart);

        Assert.Equal(JobRunStatus.Lost, status);
        Assert.Equal("no finish reported within 1h 10m", reason);
    }

    // Prod: PlayerAchievementWatcher ran 13.5 min on a 10-min interval.
    [Fact]
    public void Slow_cycle_of_a_short_interval_watcher_stays_running()
    {
        var limit = JobRunRules.ForInterval(TimeSpan.FromMinutes(10));
        var run = Run(JobRunStatus.Running, Now.AddMinutes(-13.5));

        var (status, _) = JobRunStatusEvaluator.Evaluate(run, false, limit, Now, Now.AddHours(-1));

        Assert.Equal(TimeSpan.FromHours(1), limit);
        Assert.Equal(JobRunStatus.Running, status);
    }

    // Prod: SteamPriceWatcher runs ~1000 min on a 24h interval.
    [Fact]
    public void Price_watcher_long_cycle_stays_running()
    {
        var limit = JobRunRules.ForInterval(TimeSpan.FromHours(24));
        var run = Run(JobRunStatus.Running, Now.AddMinutes(-1000), job: "SteamPriceWatcher");

        var (status, _) = JobRunStatusEvaluator.Evaluate(run, false, limit, Now, Now.AddDays(-2));

        Assert.Equal(JobRunStatus.Running, status);
    }

    [Fact]
    public void Newest_first_ignores_skipped_rows_as_newer_runs()
    {
        var running = Run(JobRunStatus.Running, Now.AddMinutes(-30), JobRunProducer.ExternalCollector, "steamdb_followers");
        var skipped = Run(JobRunStatus.Skipped, Now.AddMinutes(-10), JobRunProducer.ExternalCollector, "steamdb_followers");
        var older = Run(JobRunStatus.Running, Now.AddHours(-30), JobRunProducer.ExternalCollector, "steamdb_followers");

        var result = JobRunStatusEvaluator.EvaluateNewestFirst(
            [skipped, running, older], JobRunRules.ForTimeout(7200), Now, ApiStart);

        Assert.Equal(JobRunStatus.Skipped, result[0].Status);
        Assert.Equal(JobRunStatus.Running, result[1].Status);
        Assert.Equal(JobRunStatus.Lost, result[2].Status);
        Assert.Equal(JobRunStatusEvaluator.ReasonNewerRun, result[2].LostReason);
    }
}
