using Cato.API.Models.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;

namespace Cato.API.Services.Handlers.JobRuns;

public class ReportJobRunHandler : IRequestHandler<ReportJobRunCommand, JobRunDto>
{
    private readonly CatoDbContext _db;

    public ReportJobRunHandler(CatoDbContext db) => _db = db;

    public async Task<JobRunDto> Handle(ReportJobRunCommand request, CancellationToken ct)
    {
        // Npgsql rejects DateTimeKind.Unspecified for timestamptz columns; JSON-bound
        // DateTimes arrive Unspecified, so pin them to UTC (callers send UTC).
        var start = DateTime.SpecifyKind(request.StartTime, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(request.EndTime, DateTimeKind.Utc);

        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobName = request.JobName,
            Producer = request.Producer,
            StartTime = start,
            EndTime = end,
            DurationMs = (long)(end - start).TotalMilliseconds,
            Status = request.Status,
            MetricsJson = request.Metrics?.GetRawText(),
            ErrorMessage = request.ErrorMessage,
        };

        _db.JobRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        return JobRunMapper.ToDto(run);
    }
}
