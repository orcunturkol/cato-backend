using System.Text.Json;
using Cato.API.Models.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.JobRuns;

public class GetJobRunsHandler : IRequestHandler<GetJobRunsQuery, List<JobRunDto>>
{
    private readonly CatoDbContext _db;

    public GetJobRunsHandler(CatoDbContext db) => _db = db;

    public async Task<List<JobRunDto>> Handle(GetJobRunsQuery request, CancellationToken ct)
    {
        var query = _db.JobRuns.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.JobName))
            query = query.Where(j => j.JobName == request.JobName);
        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(j => j.Status == request.Status);

        var limit = Math.Clamp(request.Limit, 1, 500);

        var rows = await query
            .OrderByDescending(j => j.StartTime)
            .Take(limit)
            .ToListAsync(ct);

        // Parse MetricsJson -> JsonElement in memory (EF can't translate it).
        return rows.Select(JobRunMapper.ToDto).ToList();
    }
}

/// <summary>Shared <see cref="JobRun"/> -> <see cref="JobRunDto"/> mapping (parses the jsonb metrics bag).</summary>
internal static class JobRunMapper
{
    public static JobRunDto ToDto(JobRun run)
    {
        JsonElement? metrics = null;
        if (!string.IsNullOrWhiteSpace(run.MetricsJson))
        {
            using var doc = JsonDocument.Parse(run.MetricsJson);
            metrics = doc.RootElement.Clone();
        }

        return new JobRunDto(
            run.Id,
            run.JobName,
            run.Producer,
            run.StartTime,
            run.EndTime,
            run.DurationMs,
            run.Status,
            metrics,
            run.ErrorMessage);
    }
}
