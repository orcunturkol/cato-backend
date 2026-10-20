using Cato.API.DTOs;
using Cato.API.Models.ScheduledJobs;
using Cato.API.Services.JobRuns;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.ScheduledJobs;

/// <summary>What a schedule edit would do — errors, overlaps and next runs — without saving it.</summary>
public class PreviewScheduleHandler : IRequestHandler<PreviewScheduleQuery, Result<SchedulePreviewDto>>
{
    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public PreviewScheduleHandler(CatoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<Result<SchedulePreviewDto>> Handle(PreviewScheduleQuery query, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var job = await _db.ScheduledJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Name == query.Name, ct);
        if (job is null)
            return Result<SchedulePreviewDto>.Failure(UpdateScheduledJobHandler.NotFound);

        var request = query.Request;
        var settings = new ScheduleSettings(
            ScheduledJobRules.JoinCron(ScheduledJobRules.SplitCron(request.Cron)),
            request.Enabled, request.TimeoutSeconds, request.AlertFailedRatio, request.AlertMinItems);

        var errors = ScheduledJobRules.Validate(settings, now);
        var others = await _db.ScheduledJobs.AsNoTracking()
            .Where(j => j.AgentGroup == job.AgentGroup && j.Name != job.Name)
            .ToListAsync(ct);
        var warnings = errors.Count == 0 ? ScheduledJobRules.OverlapWarnings(job, settings, others, now) : [];
        var nextRuns = errors.Count == 0 && settings.Enabled ? ScheduledJobRules.NextRuns(settings.Cron, now) : [];

        return Result<SchedulePreviewDto>.Success(new SchedulePreviewDto(errors.Count == 0, errors, warnings, nextRuns));
    }
}
