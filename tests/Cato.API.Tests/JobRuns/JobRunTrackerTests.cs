using Cato.Domain.Entities;
using Cato.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.JobRuns;

public class JobRunTrackerTests : JobRunsDbTestBase
{
    private readonly CancellationTokenSource _stopping = new();

    private JobRunTracker Tracker()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        return new JobRunTracker(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JobRunTracker>.Instance,
            new Lifetime(_stopping.Token));
    }

    private JobRun Row()
    {
        using var db = NewContext();
        return db.JobRuns.Single();
    }

    [Fact]
    public async Task A_cycle_that_fails_while_the_app_stops_is_interrupted_not_failed()
    {
        // 2026-10-21: a deploy cancelled a PlayerAchievementWatcher cycle, it was stored as
        // Failed ("The operation was canceled.") and the first alert cycle reported it.
        await using (var job = await Tracker().StartAsync(BackendJobNames.PlayerAchievementWatcher))
        {
            _stopping.Cancel();
            job.Fail("The operation was canceled.");
        }

        var row = Row();
        Assert.Equal(JobRunStatus.Interrupted, row.Status);
        Assert.Equal("stopped with the app: The operation was canceled.", row.ErrorMessage);
    }

    [Fact]
    public async Task A_cycle_that_fails_while_the_app_runs_is_failed()
    {
        await using (var job = await Tracker().StartAsync(BackendJobNames.PlayerAchievementWatcher))
            job.Fail("Steam answered 500");

        Assert.Equal((JobRunStatus.Failed, "Steam answered 500"), (Row().Status, Row().ErrorMessage));
    }

    private sealed class Lifetime(CancellationToken stopping) : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => stopping;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
