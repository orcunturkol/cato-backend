using Cato.API.DTOs;
using Cato.API.Models.ScheduledJobs;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.ScheduledJobs;

/// <summary>Saves a schedule edit and its change-log row in one transaction.</summary>
public class UpdateScheduledJobHandler : IRequestHandler<UpdateScheduledJobCommand, Result<ScheduledJobUpdateResult>>
{
    public const string NotFound = "not_found";

    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public UpdateScheduledJobHandler(CatoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<Result<ScheduledJobUpdateResult>> Handle(UpdateScheduledJobCommand command, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var job = await _db.ScheduledJobs.FirstOrDefaultAsync(j => j.Name == command.Name, ct);
        if (job is null)
            return Result<ScheduledJobUpdateResult>.Failure(NotFound);

        var watcher = job.ManagedBy == ScheduledJobManagedBy.Worker;
        if (!watcher && job.ManagedBy != ScheduledJobManagedBy.Dispatcher)
        {
            return Result<ScheduledJobUpdateResult>.Failure(
                $"'{job.Name}' still runs from the host crontab, which ignores this edit. " +
                $"It becomes editable when the {job.AgentGroup} agent takes over.");
        }

        var request = command.Request;
        // A watcher reads its row before each cycle: it has an interval, no cron, no time limit.
        var settings = watcher
            ? new ScheduleSettings(null, request.Enabled, job.TimeoutSeconds,
                request.AlertFailedRatio, request.AlertMinItems, request.IntervalMinutes)
            : new ScheduleSettings(
                ScheduledJobRules.JoinCron(ScheduledJobRules.SplitCron(request.Cron)),
                request.Enabled,
                request.TimeoutSeconds,
                request.AlertFailedRatio,
                request.AlertMinItems);

        var errors = watcher ? ScheduledJobRules.ValidateWatcher(settings) : ScheduledJobRules.Validate(settings, now);
        if (errors.Count > 0)
            return Result<ScheduledJobUpdateResult>.Failure(string.Join(" ", errors));

        var before = ScheduledJobRules.Of(job);
        job.Cron = settings.Cron;
        job.Enabled = settings.Enabled;
        job.TimeoutSeconds = settings.TimeoutSeconds;
        job.AlertFailedRatio = settings.AlertFailedRatio;
        job.AlertMinItems = settings.AlertMinItems;
        job.IntervalMinutes = settings.IntervalMinutes;
        job.UpdatedAt = now;
        job.UpdatedBy = request.ChangedBy;

        _db.ScheduledJobChanges.Add(new ScheduledJobChange
        {
            Id = Guid.NewGuid(),
            JobName = job.Name,
            ChangedAt = now,
            ChangedBy = request.ChangedBy,
            Reason = request.Reason,
            OldJson = ScheduledJobMapping.ToJson(before),
            NewJson = ScheduledJobMapping.ToJson(settings),
        });
        await _db.SaveChangesAsync(ct);

        var warnings = new List<string>();
        if (!watcher)
        {
            var others = await _db.ScheduledJobs.AsNoTracking()
                .Where(j => j.AgentGroup == job.AgentGroup && j.Name != job.Name)
                .ToListAsync(ct);
            warnings = ScheduledJobRules.OverlapWarnings(job, settings, others, now);
        }

        return Result<ScheduledJobUpdateResult>.Success(
            new ScheduledJobUpdateResult(ScheduledJobMapping.ToDto(job, now), warnings));
    }
}
