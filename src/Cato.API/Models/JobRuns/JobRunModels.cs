using System.Text.Json;
using MediatR;

namespace Cato.API.Models.JobRuns;

/// <summary>
/// Report a completed job run from an external producer (e.g. the
/// catoptric-data-collector orchestrators). <see cref="Metrics"/> is an arbitrary
/// JSON object of per-job counters, stored verbatim in the job_run.MetricsJson jsonb column.
/// </summary>
public record ReportJobRunCommand(
    string JobName,
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    JsonElement? Metrics,
    string? ErrorMessage,
    string Producer = "external-collector") : IRequest<JobRunDto>;

public record GetJobRunsQuery(string? JobName, string? Status, int Limit = 50)
    : IRequest<List<JobRunDto>>;

public record JobRunDto(
    Guid Id,
    string JobName,
    string Producer,
    DateTime StartTime,
    DateTime? EndTime,
    long? DurationMs,
    string Status,
    JsonElement? Metrics,
    string? ErrorMessage);
