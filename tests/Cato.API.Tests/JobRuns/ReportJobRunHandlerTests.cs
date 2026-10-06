using System.Text.Json;
using Cato.API.Models.JobRuns;
using Cato.API.Services.Handlers.JobRuns;
using Cato.API.Validators.JobRuns;
using Cato.Domain.Entities;

namespace Cato.API.Tests.JobRuns;

public class ReportJobRunHandlerTests : JobRunsDbTestBase
{
    private static readonly DateTime Start = new(2026, 10, 6, 3, 30, 0, DateTimeKind.Unspecified);

    private static JsonElement Metrics(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<Cato.API.DTOs.Result<JobRunDto>> SendAsync(ReportJobRunCommand command)
    {
        await using var db = NewContext();
        return await new ReportJobRunHandler(db).Handle(command, CancellationToken.None);
    }

    private List<JobRun> Rows()
    {
        using var db = NewContext();
        return db.JobRuns.ToList();
    }

    [Fact]
    public async Task Report_without_id_inserts_a_finished_run_like_before()
    {
        var result = await SendAsync(new ReportJobRunCommand(
            "special_events", Start, Start.AddSeconds(40), JobRunStatus.Succeeded,
            Metrics("""{"events_found":15}"""), null));

        Assert.True(result.IsSuccess);
        var row = Assert.Single(Rows());
        Assert.Equal(40_000, row.DurationMs);
        Assert.Equal(JobRunProducer.ExternalCollector, row.Producer);
        Assert.Contains("events_found", row.MetricsJson);
    }

    [Fact]
    public async Task Start_then_finish_with_the_same_id_updates_one_row()
    {
        var id = Guid.NewGuid();

        await SendAsync(new ReportJobRunCommand("steamdb", Start, null, JobRunStatus.Running, null, null, Id: id));
        var running = Assert.Single(Rows());
        Assert.Equal(JobRunStatus.Running, running.Status);
        Assert.Null(running.EndTime);

        var result = await SendAsync(new ReportJobRunCommand(
            "steamdb", Start, Start.AddMinutes(20), JobRunStatus.PartialSuccess,
            Metrics("""{"parsed":100,"row_errors":2}"""), "2 row errors", Id: id));

        Assert.True(result.IsSuccess);
        var row = Assert.Single(Rows());
        Assert.Equal(id, row.Id);
        Assert.Equal(JobRunStatus.PartialSuccess, row.Status);
        Assert.Equal(20 * 60_000, row.DurationMs);
        Assert.Equal("2 row errors", row.ErrorMessage);
    }

    [Fact]
    public async Task Finish_without_a_prior_start_creates_the_row()
    {
        var id = Guid.NewGuid();

        var result = await SendAsync(new ReportJobRunCommand(
            "ccu", Start, Start.AddMinutes(3), JobRunStatus.Succeeded, null, null, Id: id));

        Assert.True(result.IsSuccess);
        Assert.Equal(id, Assert.Single(Rows()).Id);
    }

    [Fact]
    public async Task Late_running_report_does_not_reopen_a_finished_run()
    {
        var id = Guid.NewGuid();
        await SendAsync(new ReportJobRunCommand("ccu", Start, Start.AddMinutes(3), JobRunStatus.Failed, null, "boom", Id: id));

        var result = await SendAsync(new ReportJobRunCommand("ccu", Start, null, JobRunStatus.Running, null, null, Id: id));

        Assert.True(result.IsSuccess);
        var row = Assert.Single(Rows());
        Assert.Equal(JobRunStatus.Failed, row.Status);
        Assert.Equal("boom", row.ErrorMessage);
    }

    [Fact]
    public async Task Id_of_another_job_is_a_conflict()
    {
        var id = Guid.NewGuid();
        await SendAsync(new ReportJobRunCommand("ccu", Start, null, JobRunStatus.Running, null, null, Id: id));

        var result = await SendAsync(new ReportJobRunCommand(
            "steamdb", Start, Start.AddMinutes(1), JobRunStatus.Succeeded, null, null, Id: id));

        Assert.False(result.IsSuccess);
        Assert.Equal("ccu", Assert.Single(Rows()).JobName);
    }

    [Fact]
    public async Task Offset_timestamps_are_stored_as_utc()
    {
        // System.Text.Json binds "+03:00" timestamps as local time.
        var local = new DateTimeOffset(2026, 10, 6, 6, 30, 0, TimeSpan.FromHours(3)).LocalDateTime;

        await SendAsync(new ReportJobRunCommand("ccu", local, local.AddMinutes(1), JobRunStatus.Succeeded, null, null));

        Assert.Equal(new DateTime(2026, 10, 6, 3, 30, 0), Assert.Single(Rows()).StartTime);
    }

    [Theory]
    [InlineData(JobRunStatus.Running, false, true)]
    [InlineData(JobRunStatus.Succeeded, false, false)]
    [InlineData(JobRunStatus.Succeeded, true, true)]
    [InlineData(JobRunStatus.Skipped, true, true)]
    [InlineData(JobRunStatus.Interrupted, true, true)]
    [InlineData(JobRunStatus.Lost, true, false)]
    [InlineData("Bogus", true, false)]
    public void Validator_requires_an_end_time_for_finished_runs(string status, bool withEnd, bool valid)
    {
        var command = new ReportJobRunCommand(
            "ccu", Start, withEnd ? Start.AddMinutes(1) : null, status, null, null);

        Assert.Equal(valid, new ReportJobRunValidator().Validate(command).IsValid);
    }

    [Fact]
    public void Validator_rejects_an_end_before_the_start_and_non_object_metrics()
    {
        var validator = new ReportJobRunValidator();

        Assert.False(validator.Validate(new ReportJobRunCommand(
            "ccu", Start, Start.AddMinutes(-1), JobRunStatus.Succeeded, null, null)).IsValid);
        Assert.False(validator.Validate(new ReportJobRunCommand(
            "ccu", Start, Start.AddMinutes(1), JobRunStatus.Succeeded, Metrics("[1,2]"), null)).IsValid);
    }
}
