using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.JobRuns;

public class JobFreshnessTests : JobRunsDbTestBase
{
    // Base clock: 2026-10-06 15:21 UTC.

    private void Add(params object[] rows)
    {
        using var db = NewContext();
        db.AddRange(rows);
        db.SaveChanges();
    }

    private async Task<List<JobFreshnessDto>> CheckAsync(DateTime? now = null)
    {
        await using var db = NewContext();
        return await new JobFreshnessService(db, NewCatalog(db), NullLogger<JobFreshnessService>.Instance)
            .CheckAsync(now ?? Now, default);
    }

    private static ScheduledJob Job(string name, bool enabled = true) => new()
    {
        Name = name, Label = name, AgentGroup = JobAgentGroups.Steamworks, Module = $"orchestrators.run_{name}",
        Args = "", Cron = "30 11 * * *", Enabled = enabled, TimeoutSeconds = 900,
        ManagedBy = ScheduledJobManagedBy.Dispatcher, UpdatedAt = Now.AddDays(-1),
    };

    private static SteamworksNavStat NavStat(DateOnly periodEnd) => new()
    {
        Id = Guid.NewGuid(), PublisherId = 216530, AppId = 2206380, PeriodStart = periodEnd, PeriodEnd = periodEnd,
        Attribution = "all", Region = "All Regions", Impressions = 10, Visits = 1,
    };

    [Fact]
    public async Task Every_check_runs_on_the_database()
    {
        var results = await CheckAsync();

        Assert.Equal(JobFreshnessChecks.All.Count, results.Count);
        Assert.All(results, r => Assert.Null(r.Error));
    }

    [Fact]
    public async Task Data_older_than_the_limit_is_stale_and_newer_data_is_fresh()
    {
        Add(Job("steamworks_navstats"), NavStat(new DateOnly(2026, 10, 2)));

        var old = (await CheckAsync()).Single(r => r.JobName == "steamworks_navstats");

        Assert.True(old.Stale);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), old.NewestAt);
        Assert.Equal(111.4, old.AgeHours);  // 4 days 15:21

        Add(NavStat(new DateOnly(2026, 10, 5)));
        Assert.False((await CheckAsync()).Single(r => r.JobName == "steamworks_navstats").Stale);
    }

    [Fact]
    public async Task An_empty_table_is_stale_only_while_its_job_is_enabled()
    {
        Add(Job("steamworks_navstats"), Job("steamworks_traffic", enabled: false));

        var results = await CheckAsync();

        var navstats = results.Single(r => r.JobName == "steamworks_navstats");
        Assert.True(navstats.Stale);
        Assert.Null(navstats.NewestAt);
        var traffic = results.Single(r => r.JobName == "steamworks_traffic");
        Assert.False(traffic.Stale);
        Assert.False(traffic.Enabled);
    }

    [Fact]
    public async Task The_cache_runs_the_checks_at_most_every_ten_minutes()
    {
        Add(Job("steamworks_navstats"));
        var cache = new JobFreshnessCache();
        await using var db = NewContext();
        var service = new JobFreshnessService(db, NewCatalog(db), NullLogger<JobFreshnessService>.Instance);

        var first = await cache.GetAsync(service, Now, default);
        Add(NavStat(new DateOnly(2026, 10, 6)));
        var cached = await cache.GetAsync(service, Now.AddMinutes(9), default);
        var fresh = await cache.GetAsync(service, Now.AddMinutes(10), default);

        Assert.Equal(Now, cached.CheckedAt);
        Assert.Same(first.Results, cached.Results);
        Assert.False(fresh.Results.Single(r => r.JobName == "steamworks_navstats").Stale);
    }
}
