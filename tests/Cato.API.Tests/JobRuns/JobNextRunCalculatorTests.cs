using Cato.API.Models.JobRuns;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;

namespace Cato.API.Tests.JobRuns;

public class JobNextRunCalculatorTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 15, 21, 0, DateTimeKind.Utc);
    private static readonly DateTime ApiStart = new(2026, 10, 6, 14, 17, 40, DateTimeKind.Utc);

    private static JobDefinition Cron(params string[] crons) => new(
        "ccu", JobRunProducer.ExternalCollector, null, JobScheduleKind.Cron, crons, null,
        true, null, null, JobRunRules.DefaultStaleAfter, TimeSpan.Zero);

    private static JobDefinition Interval(int minutes, bool enabled = true, string? reason = null) => new(
        "GameEnrichmentWatcher", JobRunProducer.CatoBackend, null, JobScheduleKind.Interval, [], minutes,
        enabled, reason, null, JobRunRules.ForInterval(TimeSpan.FromMinutes(minutes)), TimeSpan.FromMinutes(1));

    private static JobRunDto Run(DateTime start, DateTime? end, string effective) => new(
        Guid.NewGuid(), "GameEnrichmentWatcher", JobRunProducer.CatoBackend, start, end,
        end is null ? null : (long)(end.Value - start).TotalMilliseconds,
        effective == JobRunStatus.Lost ? JobRunStatus.Running : effective, null, null, effective);

    [Fact]
    public void Cron_every_four_hours_gives_the_next_slot_in_utc()
    {
        var (next, note) = JobNextRunCalculator.Calculate(Cron("0 */4 * * *"), null, Now, ApiStart);

        Assert.Equal(new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc), next);
        Assert.Equal(DateTimeKind.Utc, next!.Value.Kind);
        Assert.Null(note);
    }

    [Fact]
    public void Daily_cron_already_past_today_rolls_to_tomorrow()
    {
        var (next, _) = JobNextRunCalculator.Calculate(Cron("30 3 * * *"), null, Now, ApiStart);

        Assert.Equal(new DateTime(2026, 10, 7, 3, 30, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Several_cron_lines_take_the_earliest()
    {
        var (next, _) = JobNextRunCalculator.Calculate(Cron("0 6 * * *", "15 * * * *"), null, Now, ApiStart);

        Assert.Equal(new DateTime(2026, 10, 6, 16, 15, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void Disabled_job_has_no_next_run_and_shows_why()
    {
        var job = Cron("0 4 * * *") with { Enabled = false, DisabledReason = "commented out in crontab" };

        var (next, note) = JobNextRunCalculator.Calculate(job, null, Now, ApiStart);

        Assert.Null(next);
        Assert.Equal("commented out in crontab", note);
    }

    [Fact]
    public void Interval_job_waits_the_interval_after_the_last_end()
    {
        var last = Run(Now.AddMinutes(-40), Now.AddMinutes(-20), JobRunStatus.PartialSuccess);

        var (next, note) = JobNextRunCalculator.Calculate(Interval(60), last, Now, ApiStart);

        Assert.Equal(Now.AddMinutes(40), next);
        Assert.Null(note);
    }

    [Fact]
    public void Interval_job_running_now_has_no_fixed_next_run()
    {
        var last = Run(Now.AddMinutes(-5), null, JobRunStatus.Running);

        var (next, note) = JobNextRunCalculator.Calculate(Interval(60), last, Now, ApiStart);

        Assert.Null(next);
        Assert.Equal(JobNextRunCalculator.NoteAfterCurrentRun, note);
    }

    [Fact]
    public void Interval_job_whose_last_run_predates_the_api_start_expects_a_first_run()
    {
        var last = Run(ApiStart.AddHours(-1), ApiStart.AddMinutes(-30), JobRunStatus.Succeeded);
        var now = ApiStart.AddSeconds(30);

        var (next, note) = JobNextRunCalculator.Calculate(Interval(60), last, now, ApiStart);

        Assert.Equal(ApiStart.AddMinutes(1), next);
        Assert.Equal(JobNextRunCalculator.NoteFirstRunSinceStart, note);
    }

    [Fact]
    public void Interval_job_past_its_slot_is_overdue()
    {
        var last = Run(Now.AddMinutes(-62), Now.AddMinutes(-61), JobRunStatus.Succeeded);

        var (next, note) = JobNextRunCalculator.Calculate(Interval(30), last, Now, ApiStart);

        Assert.Equal(Now.AddMinutes(-31), next);
        Assert.Equal(JobNextRunCalculator.NoteOverdue, note);
    }

    [Fact]
    public void First_run_after_start_that_never_came_is_overdue()
    {
        var (next, note) = JobNextRunCalculator.Calculate(Interval(60), null, Now, ApiStart);

        Assert.Equal(ApiStart.AddMinutes(1), next);
        Assert.Equal($"{JobNextRunCalculator.NoteFirstRunSinceStart}, {JobNextRunCalculator.NoteOverdue}", note);
    }

    [Theory]
    [InlineData("0 3 * * *", true)]
    [InlineData("*/15 * * * *", true)]
    [InlineData("0 */4 * * *", true)]
    [InlineData("not a cron", false)]
    [InlineData("0 3 * *", false)]
    [InlineData("", false)]
    public void Cron_parser_accepts_five_field_expressions_only(string expression, bool valid)
    {
        Assert.Equal(valid, JobNextRunCalculator.TryParseCron(expression, out _));
    }
}
