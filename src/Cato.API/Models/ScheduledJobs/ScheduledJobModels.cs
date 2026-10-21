using System.Text.Json;
using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.ScheduledJobs;

/// <summary>
/// One collector job as the Jobs page edits it. <see cref="Editable"/> is false while the
/// host crontab still starts the job: an edit would have no effect there.
/// </summary>
public record ScheduledJobDto(
    string Name,
    string? Label,
    string AgentGroup,
    string Module,
    string Args,
    string? Cron,
    bool Enabled,
    int TimeoutSeconds,
    decimal AlertFailedRatio,
    int AlertMinItems,
    string ManagedBy,
    bool Editable,
    List<DateTime> NextRuns,
    DateTime UpdatedAt,
    string? UpdatedBy,
    int? IntervalMinutes = null);

public record GetScheduledJobsQuery : IRequest<List<ScheduledJobDto>>;

/// <summary>
/// Body of PUT /api/scheduled-jobs/{name}. <see cref="Cron"/> is UTC; several expressions are
/// separated by "|"; empty means the job runs on request only. A backend watcher takes
/// <see cref="IntervalMinutes"/> instead of a cron and a time limit.
/// </summary>
public record UpdateScheduledJobRequest(
    string? Cron,
    bool Enabled,
    int TimeoutSeconds,
    decimal AlertFailedRatio,
    int AlertMinItems,
    string? Reason,
    string? ChangedBy,
    int? IntervalMinutes = null);

public record UpdateScheduledJobCommand(string Name, UpdateScheduledJobRequest Request)
    : IRequest<Result<ScheduledJobUpdateResult>>;

/// <summary>The saved job and the overlaps the new schedule causes in its agent group.</summary>
public record ScheduledJobUpdateResult(ScheduledJobDto Job, List<string> Warnings);

/// <summary>Body of POST /api/scheduled-jobs/{name}/preview: check a schedule before saving it.</summary>
public record PreviewScheduleRequest(string? Cron, bool Enabled, int TimeoutSeconds, decimal AlertFailedRatio, int AlertMinItems);

public record PreviewScheduleQuery(string Name, PreviewScheduleRequest Request) : IRequest<Result<SchedulePreviewDto>>;

public record SchedulePreviewDto(bool Valid, List<string> Errors, List<string> Warnings, List<DateTime> NextRuns);

public record GetScheduledJobChangesQuery(string Name, int Limit = 50) : IRequest<List<ScheduledJobChangeDto>>;

public record ScheduledJobChangeDto(
    Guid Id,
    string JobName,
    DateTime ChangedAt,
    string? ChangedBy,
    string? Reason,
    JsonElement Old,
    JsonElement New);
