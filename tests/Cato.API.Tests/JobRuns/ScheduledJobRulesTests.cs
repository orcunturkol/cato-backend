using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;

namespace Cato.API.Tests.JobRuns;

public class ScheduledJobRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static ScheduleSettings Settings(string? cron, int timeoutSeconds, decimal ratio = 0.10m, int minItems = 1) =>
        new(cron, true, timeoutSeconds, ratio, minItems);

    private static ScheduledJob Job(string name, string group, string? cron, int timeoutSeconds, bool enabled = true) => new()
    {
        Name = name, Label = name, AgentGroup = group, Module = "m", Cron = cron,
        TimeoutSeconds = timeoutSeconds, Enabled = enabled,
    };

    [Theory]
    [InlineData("0 */4 * * *", 3600)]
    [InlineData("45 0,1,6-23 * * *", 3000)]
    [InlineData("0 10 * * *", 2700)]
    [InlineData(null, 1200)]
    public void Todays_schedules_are_valid(string? cron, int timeoutSeconds) =>
        Assert.Empty(ScheduledJobRules.Validate(Settings(cron, timeoutSeconds), Now));

    [Fact]
    public void A_bad_cron_expression_is_named()
    {
        var errors = ScheduledJobRules.Validate(Settings("0 25 * * *", 600), Now);

        Assert.Contains(errors, e => e.Contains("'0 25 * * *' is not a 5-field cron expression"));
    }

    [Fact]
    public void The_interval_must_be_longer_than_the_time_limit()
    {
        var errors = ScheduledJobRules.Validate(Settings("*/30 * * * *", 3600), Now);

        Assert.Single(errors);
        Assert.Contains("every 30 minutes, but its time limit is 60 minutes", errors[0]);
    }

    [Fact]
    public void The_shortest_gap_counts_across_all_expressions()
    {
        // Each expression alone is daily; together they start ten minutes apart.
        var errors = ScheduledJobRules.Validate(Settings("0 10 * * * | 10 10 * * *", 1200), Now);

        Assert.Contains(errors, e => e.Contains("every 10 minutes"));
    }

    [Theory]
    [InlineData(30, 0.10, 1, "time limit")]
    [InlineData(90000, 0.10, 1, "time limit")]
    [InlineData(600, 0.0, 1, "alert threshold")]
    [InlineData(600, 1.5, 1, "alert threshold")]
    [InlineData(600, 0.10, 0, "minimum failed items")]
    public void Limits_and_alert_settings_have_ranges(int timeoutSeconds, double ratio, int minItems, string expected)
    {
        var errors = ScheduledJobRules.Validate(Settings("0 3 * * *", timeoutSeconds, (decimal)ratio, minItems), Now);

        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Fact]
    public void Next_runs_merge_several_expressions_in_time_order()
    {
        var runs = ScheduledJobRules.NextRuns("0 12 * * * | 0 10 * * *", Now, 4);

        Assert.Equal(
        [
            new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc),
        ], runs);
    }

    [Fact]
    public void Cron_is_stored_trimmed_and_joined()
    {
        Assert.Equal("0 3 * * * | 30 4 * * *", ScheduledJobRules.JoinCron(ScheduledJobRules.SplitCron(" 0 3 * * * |30 4 * * *| ")));
        Assert.Null(ScheduledJobRules.JoinCron(ScheduledJobRules.SplitCron("  ")));
    }

    [Fact]
    public void Store_traffic_can_overlap_nav_stats_in_the_steamworks_agent()
    {
        var traffic = Job("steamworks_traffic", JobAgentGroups.Steamworks, "0 11 * * *", 2700);
        var others = new[]
        {
            Job("steamworks_navstats", JobAgentGroups.Steamworks, "30 11 * * *", 900),
            Job("steamworks_wishlist", JobAgentGroups.Steamworks, "0 10 * * *", 2700),
            Job("ccu", JobAgentGroups.Light, "0 */4 * * *", 3600),
            Job("steamworks_paused", JobAgentGroups.Steamworks, "0 11 * * *", 600, enabled: false),
        };

        var warnings = ScheduledJobRules.OverlapWarnings(traffic, ScheduledJobRules.Of(traffic), others, Now);

        var warning = Assert.Single(warnings);
        Assert.Contains("\"steamworks_navstats\" at 11:30 UTC", warning);
        Assert.Contains("runs one job at a time", warning);
    }

    [Fact]
    public void A_paused_job_warns_about_nothing()
    {
        var traffic = Job("steamworks_traffic", JobAgentGroups.Steamworks, "0 11 * * *", 2700);
        var paused = ScheduledJobRules.Of(traffic) with { Enabled = false };
        var others = new[] { Job("steamworks_navstats", JobAgentGroups.Steamworks, "30 11 * * *", 900) };

        Assert.Empty(ScheduledJobRules.OverlapWarnings(traffic, paused, others, Now));
    }
}
