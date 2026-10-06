using System.Text.Json;
using Cato.API.Models.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.JobRuns;

/// <summary>Maps stored runs to DTOs with the status to show.</summary>
public sealed class JobRunStatusResolver
{
    private readonly CatoDbContext _db;
    private readonly ApiProcessInfo _process;
    private readonly TimeProvider _time;

    public JobRunStatusResolver(CatoDbContext db, ApiProcessInfo process, TimeProvider time)
    {
        _db = db;
        _process = process;
        _time = time;
    }

    /// <summary>For an arbitrary set of rows; looks up newer runs in the database.</summary>
    public async Task<List<JobRunDto>> ToDtosAsync(
        IReadOnlyList<JobRun> rows, JobCatalogSnapshot catalog, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var latestStarts = await LatestStartsAsync(rows, ct);

        return rows.Select(run =>
        {
            var newer = latestStarts.TryGetValue((run.Producer, run.JobName), out var latest)
                        && latest > run.StartTime;
            var (status, reason) = JobRunStatusEvaluator.Evaluate(
                run, newer, catalog.StaleAfterFor(run.Producer, run.JobName), now, _process.StartedAtUtc);
            return JobRunMapper.ToDto(run, status, reason);
        }).ToList();
    }

    private async Task<Dictionary<(string, string), DateTime>> LatestStartsAsync(
        IReadOnlyList<JobRun> rows, CancellationToken ct)
    {
        var names = rows.Where(r => r.Status == JobRunStatus.Running)
            .Select(r => r.JobName).Distinct().ToList();
        if (names.Count == 0) return [];

        var latest = await _db.JobRuns.AsNoTracking()
            .Where(j => names.Contains(j.JobName) && j.Status != JobRunStatus.Skipped)
            .GroupBy(j => new { j.Producer, j.JobName })
            .Select(g => new { g.Key.Producer, g.Key.JobName, Latest = g.Max(j => j.StartTime) })
            .ToListAsync(ct);

        return latest.ToDictionary(l => (l.Producer, l.JobName), l => l.Latest);
    }
}

/// <summary>Shared <see cref="JobRun"/> to <see cref="JobRunDto"/> mapping (parses the jsonb metrics bag).</summary>
public static class JobRunMapper
{
    public static JobRunDto ToDto(JobRun run, string? effectiveStatus = null, string? lostReason = null)
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
            AsUtc(run.StartTime),
            run.EndTime is { } end ? AsUtc(end) : null,
            run.DurationMs,
            run.Status,
            metrics,
            run.ErrorMessage,
            effectiveStatus ?? run.Status,
            lostReason);
    }

    // Npgsql already returns UTC; this keeps the "Z" in JSON for other providers too.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
