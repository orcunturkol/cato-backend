using System.Text.Json;
using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.JobRuns;

/// <summary>
/// Report a job run from an external producer (e.g. the catoptric-data-collector).
/// Without <see cref="Id"/> it inserts a new row. With <see cref="Id"/> it upserts that
/// row, so a producer can report "Running" at start and the outcome at finish.
/// <see cref="EndTime"/> is required for every status except Running.
/// </summary>
public record ReportJobRunCommand(
    string JobName,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    JsonElement? Metrics,
    string? ErrorMessage,
    string Producer = "external-collector",
    Guid? Id = null,
    Guid? DispatchId = null) : IRequest<Result<JobRunDto>>;

public record GetJobRunsQuery(string? JobName, string? Status, int Limit = 50, string? Producer = null)
    : IRequest<List<JobRunDto>>;

/// <summary>
/// <see cref="Status"/> is what the producer stored. <see cref="EffectiveStatus"/> is
/// what to show: the same, except a dead "Running" row reads "Lost" (see <see cref="LostReason"/>).
/// </summary>
public record JobRunDto(
    Guid Id,
    string JobName,
    string Producer,
    DateTime StartTime,
    DateTime? EndTime,
    long? DurationMs,
    string Status,
    JsonElement? Metrics,
    string? ErrorMessage,
    string EffectiveStatus,
    string? LostReason = null,
    Guid? DispatchId = null);

public record GetJobRunsOverviewQuery(int RecentRunsPerJob = 5) : IRequest<JobRunsOverviewDto>;

public record JobRunsOverviewDto(
    DateTime GeneratedAt,
    DateTime ApiStartedAt,
    List<JobScheduleSourceDto> ScheduleSources,
    List<JobRunDto> Running,
    List<JobOverviewDto> Jobs,
    DateTime? WorkerStartedAt = null);

/// <summary>One job with its schedule, last run and next expected run.</summary>
public record JobOverviewDto(
    string JobName,
    string Producer,
    string? Label,
    string ScheduleKind,
    string? CronExpression,
    int? IntervalMinutes,
    bool Enabled,
    string? DisabledReason,
    int? TimeoutSeconds,
    int StaleAfterSeconds,
    DateTime? NextRunAt,
    string? NextRunNote,
    JobRunDto? LastRun,
    List<JobRunDto> RecentRuns);

public record JobScheduleSourceDto(
    string Producer,
    string Source,
    string? SourceHost,
    DateTime PublishedAt,
    int EntryCount);
