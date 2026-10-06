using Cato.API.Models.JobRuns;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.JobRuns;

/// <summary>Running runs, plus every job with its schedule, last run and next run.</summary>
public class GetJobRunsOverviewHandler : IRequestHandler<GetJobRunsOverviewQuery, JobRunsOverviewDto>
{
    // Jobs that ran in this window but have no schedule still get a row.
    private static readonly TimeSpan SeenWindow = TimeSpan.FromDays(30);
    private const int MaxRunningRows = 200;

    private readonly CatoDbContext _db;
    private readonly IJobCatalog _catalog;
    private readonly JobRunStatusResolver _resolver;
    private readonly ApiProcessInfo _process;
    private readonly TimeProvider _time;

    public GetJobRunsOverviewHandler(
        CatoDbContext db,
        IJobCatalog catalog,
        JobRunStatusResolver resolver,
        ApiProcessInfo process,
        TimeProvider time)
    {
        _db = db;
        _catalog = catalog;
        _resolver = resolver;
        _process = process;
        _time = time;
    }

    public async Task<JobRunsOverviewDto> Handle(GetJobRunsOverviewQuery request, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var perJob = Math.Clamp(request.RecentRunsPerJob, 1, 50);
        var catalog = await _catalog.LoadAsync(ct);

        var since = now - SeenWindow;
        var seen = await _db.JobRuns.AsNoTracking()
            .Where(j => j.StartTime >= since)
            .Select(j => new { j.Producer, j.JobName })
            .Distinct()
            .ToListAsync(ct);

        var definitions = catalog.Jobs.ToList();
        definitions.AddRange(seen
            .Where(s => catalog.Find(s.Producer, s.JobName) is null)
            .Select(s => JobDefinition.Unscheduled(s.Producer, s.JobName)));

        var jobs = new List<JobOverviewDto>(definitions.Count);
        foreach (var job in definitions)
            jobs.Add(await BuildJobAsync(job, perJob, now, ct));

        var runningRows = await _db.JobRuns.AsNoTracking()
            .Where(j => j.Status == JobRunStatus.Running)
            .OrderByDescending(j => j.StartTime)
            .Take(MaxRunningRows)
            .ToListAsync(ct);
        var running = (await _resolver.ToDtosAsync(runningRows, catalog, ct))
            .Where(r => r.EffectiveStatus == JobRunStatus.Running)
            .ToList();

        return new JobRunsOverviewDto(
            DateTime.SpecifyKind(now, DateTimeKind.Utc),
            DateTime.SpecifyKind(_process.StartedAtUtc, DateTimeKind.Utc),
            catalog.Sources.ToList(),
            running,
            jobs
                .OrderBy(j => j.NextRunAt is null)
                .ThenBy(j => j.NextRunAt)
                .ThenBy(j => j.JobName, StringComparer.Ordinal)
                .ToList());
    }

    private async Task<JobOverviewDto> BuildJobAsync(JobDefinition job, int perJob, DateTime now, CancellationToken ct)
    {
        var rows = await _db.JobRuns.AsNoTracking()
            .Where(j => j.Producer == job.Producer && j.JobName == job.JobName)
            .OrderByDescending(j => j.StartTime)
            .Take(perJob)
            .ToListAsync(ct);

        var recent = JobRunStatusEvaluator
            .EvaluateNewestFirst(rows, job.StaleAfter, now, _process.StartedAtUtc)
            .Select(e => JobRunMapper.ToDto(e.Run, e.Status, e.LostReason))
            .ToList();

        // A skip says nothing about when the job itself last ran.
        var lastRun = recent.FirstOrDefault(r => r.Status != JobRunStatus.Skipped) ?? recent.FirstOrDefault();
        var (nextRunAt, note) = JobNextRunCalculator.Calculate(job, lastRun, now, _process.StartedAtUtc);

        return new JobOverviewDto(
            job.JobName,
            job.Producer,
            job.Label,
            job.ScheduleKind,
            job.CronExpressions.Count > 0 ? string.Join(" | ", job.CronExpressions) : null,
            job.IntervalMinutes,
            job.Enabled,
            job.DisabledReason,
            job.TimeoutSeconds,
            (int)job.StaleAfter.TotalSeconds,
            nextRunAt,
            note,
            lastRun,
            recent);
    }
}
