using Cato.API.DTOs;
using Cato.API.Models.JobRuns;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;

namespace Cato.API.Services.Handlers.JobRuns;

/// <summary>
/// Inserts a run, or upserts it when the producer sends its own id. A finished row
/// is never reopened by a late "Running" report.
/// </summary>
public class ReportJobRunHandler : IRequestHandler<ReportJobRunCommand, Result<JobRunDto>>
{
    private readonly CatoDbContext _db;

    public ReportJobRunHandler(CatoDbContext db) => _db = db;

    public async Task<Result<JobRunDto>> Handle(ReportJobRunCommand request, CancellationToken ct)
    {
        // Npgsql rejects DateTimeKind.Unspecified for timestamptz columns; JSON-bound
        // DateTimes arrive Unspecified, so pin them to UTC (callers send UTC).
        var start = AsUtc(request.StartTime);
        var end = request.Status == JobRunStatus.Running || request.EndTime is null
            ? (DateTime?)null
            : AsUtc(request.EndTime.Value);

        var run = request.Id is { } id ? await _db.JobRuns.FindAsync([id], ct) : null;

        if (run is null)
        {
            run = new JobRun
            {
                Id = request.Id ?? Guid.NewGuid(),
                JobName = request.JobName,
                Producer = request.Producer,
                StartTime = start,
            };
            _db.JobRuns.Add(run);
        }
        else if (run.JobName != request.JobName || run.Producer != request.Producer)
        {
            return Result<JobRunDto>.Failure(
                $"Run {run.Id} belongs to {run.Producer}/{run.JobName}, not {request.Producer}/{request.JobName}.");
        }
        else if (run.Status != JobRunStatus.Running && request.Status == JobRunStatus.Running)
        {
            return Result<JobRunDto>.Success(JobRunMapper.ToDto(run));
        }

        run.Status = request.Status;
        run.EndTime = end;
        run.DurationMs = end is null ? null : (long)(end.Value - run.StartTime).TotalMilliseconds;
        if (request.Metrics is { } metrics)
            run.MetricsJson = metrics.GetRawText();
        if (request.ErrorMessage is not null || end is not null)
            run.ErrorMessage = request.ErrorMessage;

        await _db.SaveChangesAsync(ct);

        return Result<JobRunDto>.Success(JobRunMapper.ToDto(run));
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
