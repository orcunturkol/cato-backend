using System.Text.Json;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cato.API.Services.Dispatch;

/// <summary>Settings section "JobDispatcher".</summary>
public sealed class JobDispatcherSettings
{
    public const string SectionName = "JobDispatcher";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the dispatcher looks for due slots.</summary>
    public int IntervalSeconds { get; set; } = 20;

    /// <summary>A pending dispatch nobody claims within this time expires (agent offline).</summary>
    public int PendingExpiryHours { get; set; } = 6;

    /// <summary>A claimed dispatch counts as lost this long after its time limit.</summary>
    public int ClaimGraceMinutes { get; set; } = 10;
}

/// <summary>What an agent receives when it claims a run.</summary>
public sealed record ClaimedRun(
    Guid DispatchId,
    string JobName,
    string Module,
    string Args,
    int TimeoutSeconds,
    DateTime ScheduledAt,
    string Kind);

public enum RunRequestOutcome { Queued, NotFound, NotDispatcherManaged, AlreadyActive }

public sealed record RunRequestResult(RunRequestOutcome Outcome, Guid? DispatchId, string? Message);

public enum FinishOutcome { Recorded, NotFound, NotYourClaim }

/// <summary>
/// The job dispatcher's database work: turns due cron slots into dispatches, lets agents
/// claim and finish them, and expires the ones nobody runs. Scoped; the hosted
/// <see cref="JobDispatcherService"/> calls <see cref="DispatchDueAsync"/> on a timer, the
/// controllers call the rest.
/// </summary>
public sealed class JobDispatchService
{
    // Oldest slot the dispatcher still considers after downtime (weekly jobs fit).
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(8);
    private const int DefaultTimeoutSeconds = 3600;

    private readonly CatoDbContext _db;
    private readonly JobDispatcherSettings _settings;

    public JobDispatchService(CatoDbContext db, IOptions<JobDispatcherSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
    }

    /// <summary>
    /// One dispatch per job for its newest due slot — never a backlog. The slot must be later
    /// than the job's last scheduled dispatch and its last change: a group that just moved
    /// from cron must not repeat the slot cron already ran. A slot whose previous run is still
    /// active becomes a skipped dispatch and a Skipped run on the Jobs page.
    /// </summary>
    public async Task<int> DispatchDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        var jobs = await _db.ScheduledJobs.AsNoTracking()
            .Where(j => j.ManagedBy == ScheduledJobManagedBy.Dispatcher && j.Enabled && j.Cron != null)
            .ToListAsync(ct);
        if (jobs.Count == 0) return 0;

        var names = jobs.Select(j => j.Name).ToList();
        var lastSlots = await _db.JobDispatches.AsNoTracking()
            .Where(d => names.Contains(d.JobName) && d.Kind == JobDispatchKind.Scheduled)
            .GroupBy(d => d.JobName)
            .Select(g => new { JobName = g.Key, Last = g.Max(d => d.ScheduledAt) })
            .ToDictionaryAsync(x => x.JobName, x => x.Last, ct);
        var active = await _db.JobDispatches.AsNoTracking()
            .Where(d => names.Contains(d.JobName)
                        && (d.Status == JobDispatchStatus.Pending || d.Status == JobDispatchStatus.Claimed))
            .Select(d => d.JobName)
            .Distinct()
            .ToListAsync(ct);

        var created = 0;
        foreach (var job in jobs)
        {
            var after = Max(job.UpdatedAt, lastSlots.GetValueOrDefault(job.Name), nowUtc - Lookback);
            var slot = NewestSlot(job.Cron, after, nowUtc);
            if (slot is null) continue;

            var busy = active.Contains(job.Name);
            _db.JobDispatches.Add(new JobDispatch
            {
                Id = Guid.NewGuid(),
                JobName = job.Name,
                AgentGroup = job.AgentGroup,
                ScheduledAt = slot.Value,
                Kind = JobDispatchKind.Scheduled,
                Status = busy ? JobDispatchStatus.Skipped : JobDispatchStatus.Pending,
                Note = busy ? "previous run still active" : null,
                CreatedAt = nowUtc,
            });
            if (busy)
            {
                _db.JobRuns.Add(new JobRun
                {
                    Id = Guid.NewGuid(),
                    JobName = job.Name,
                    Producer = JobRunProducer.ExternalCollector,
                    StartTime = nowUtc,
                    EndTime = nowUtc,
                    DurationMs = 0,
                    Status = JobRunStatus.Skipped,
                    ErrorMessage = $"slot {slot.Value:yyyy-MM-dd HH:mm} UTC skipped: the previous run is still active",
                    CreatedAt = nowUtc,
                });
            }
            created++;
        }

        await _db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// Expires pending dispatches nobody claimed and marks claimed ones lost once they pass
    /// their time limit plus the grace. Either way the job can be dispatched again.
    /// </summary>
    public async Task<int> HousekeepAsync(DateTime nowUtc, CancellationToken ct)
    {
        var expireBefore = nowUtc - TimeSpan.FromHours(_settings.PendingExpiryHours);
        var expired = await _db.JobDispatches
            .Where(d => d.Status == JobDispatchStatus.Pending && d.CreatedAt < expireBefore)
            .ToListAsync(ct);
        foreach (var d in expired)
        {
            d.Status = JobDispatchStatus.Expired;
            d.Note = $"no agent claimed it within {_settings.PendingExpiryHours} h";
        }

        var claimed = await _db.JobDispatches
            .Where(d => d.Status == JobDispatchStatus.Claimed)
            .ToListAsync(ct);
        var timeouts = await _db.ScheduledJobs.AsNoTracking()
            .ToDictionaryAsync(j => j.Name, j => j.TimeoutSeconds, ct);
        var grace = TimeSpan.FromMinutes(_settings.ClaimGraceMinutes);
        var lost = claimed.Where(d =>
                d.ClaimedAt + TimeSpan.FromSeconds(timeouts.GetValueOrDefault(d.JobName, DefaultTimeoutSeconds)) + grace < nowUtc)
            .ToList();
        foreach (var d in lost)
        {
            d.Status = JobDispatchStatus.Lost;
            d.Note = "the agent did not report a finish within the time limit";
        }

        if (expired.Count + lost.Count > 0)
            await _db.SaveChangesAsync(ct);
        return expired.Count + lost.Count;
    }

    /// <summary>"Run now": a manual dispatch, unless the job still runs from cron or is already active.</summary>
    public async Task<RunRequestResult> RequestRunAsync(
        string jobName, string? requestedBy, DateTime nowUtc, CancellationToken ct)
    {
        var job = await _db.ScheduledJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Name == jobName, ct);
        if (job is null)
            return new RunRequestResult(RunRequestOutcome.NotFound, null, null);
        if (job.ManagedBy != ScheduledJobManagedBy.Dispatcher)
        {
            return new RunRequestResult(RunRequestOutcome.NotDispatcherManaged, null,
                $"'{jobName}' still runs from the host crontab; run now works once the {job.AgentGroup} agent takes over.");
        }

        var activeId = await _db.JobDispatches.AsNoTracking()
            .Where(d => d.JobName == jobName
                        && (d.Status == JobDispatchStatus.Pending || d.Status == JobDispatchStatus.Claimed))
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (activeId is not null)
            return new RunRequestResult(RunRequestOutcome.AlreadyActive, activeId, $"'{jobName}' is already waiting or running.");

        var dispatch = new JobDispatch
        {
            Id = Guid.NewGuid(),
            JobName = jobName,
            AgentGroup = job.AgentGroup,
            ScheduledAt = nowUtc,
            Kind = JobDispatchKind.Manual,
            RequestedBy = requestedBy,
            Status = JobDispatchStatus.Pending,
            CreatedAt = nowUtc,
        };
        _db.JobDispatches.Add(dispatch);
        await _db.SaveChangesAsync(ct);
        return new RunRequestResult(RunRequestOutcome.Queued, dispatch.Id, null);
    }

    /// <summary>
    /// Hands the oldest pending dispatch of the group to the agent — but never while the group
    /// already runs one, so two agents of one group (a deploy overlap) cannot run two Chrome
    /// jobs on one profile. The claim is a conditional update, so two agents never get the
    /// same dispatch. The Redis run locks stay as a second guard.
    /// </summary>
    public async Task<ClaimedRun?> ClaimAsync(
        string group, string agentId, string? host, DateTime nowUtc, CancellationToken ct)
    {
        await TouchAgentAsync(group, agentId, host, null, nowUtc, setDispatch: false, ct);

        // An agent claims only when it is idle. A run it still holds died with its process
        // (out of memory, kill -9): close it now, or the group waits for the time limit.
        await _db.JobDispatches
            .Where(d => d.ClaimedBy == agentId && d.Status == JobDispatchStatus.Claimed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, JobDispatchStatus.Lost)
                .SetProperty(d => d.Note, "the agent claimed again, so this run had stopped without a finish"), ct);

        var busy = await _db.JobDispatches.AnyAsync(
            d => d.AgentGroup == group && d.Status == JobDispatchStatus.Claimed, ct);
        if (busy) return null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var next = await _db.JobDispatches.AsNoTracking()
                .Where(d => d.AgentGroup == group && d.Status == JobDispatchStatus.Pending)
                .OrderBy(d => d.ScheduledAt)
                .FirstOrDefaultAsync(ct);
            if (next is null) return null;

            var won = await _db.JobDispatches
                .Where(d => d.Id == next.Id && d.Status == JobDispatchStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, JobDispatchStatus.Claimed)
                    .SetProperty(d => d.ClaimedBy, agentId)
                    .SetProperty(d => d.ClaimedAt, nowUtc), ct);
            if (won != 1) continue;

            var job = await _db.ScheduledJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Name == next.JobName, ct);
            if (job is null)
            {
                await _db.JobDispatches.Where(d => d.Id == next.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Status, JobDispatchStatus.Expired)
                    .SetProperty(d => d.Note, "the job no longer exists"), ct);
                continue;
            }

            await TouchAgentAsync(group, agentId, host, next.Id, nowUtc, setDispatch: true, ct);
            return new ClaimedRun(next.Id, job.Name, job.Module, job.Args, job.TimeoutSeconds,
                DateTime.SpecifyKind(next.ScheduledAt, DateTimeKind.Utc), next.Kind);
        }

        return null;
    }

    public async Task HeartbeatAsync(string group, string agentId, string? host, Guid? dispatchId, DateTime nowUtc, CancellationToken ct)
    {
        // A heartbeat sent just before the finish can name a run that already ended: ignore that run.
        var current = dispatchId is { } id && await _db.JobDispatches.AnyAsync(
            d => d.Id == id && d.ClaimedBy == agentId && d.Status == JobDispatchStatus.Claimed, ct)
            ? dispatchId
            : null;
        await TouchAgentAsync(group, agentId, host, current, nowUtc, setDispatch: true, ct);
    }

    public async Task<FinishOutcome> FinishAsync(
        Guid dispatchId, string agentId, int exitCode, string? note, DateTime nowUtc, CancellationToken ct)
    {
        var dispatch = await _db.JobDispatches.FirstOrDefaultAsync(d => d.Id == dispatchId, ct);
        if (dispatch is null) return FinishOutcome.NotFound;
        // The claim is a direct UPDATE that bypasses the change tracker: read the row fresh.
        await _db.Entry(dispatch).ReloadAsync(ct);
        if (dispatch.ClaimedBy != agentId) return FinishOutcome.NotYourClaim;

        dispatch.Status = JobDispatchStatus.Finished;
        dispatch.FinishedAt = nowUtc;
        dispatch.ExitCode = exitCode;
        dispatch.Note = note;

        var agent = await _db.JobAgents.FirstOrDefaultAsync(a => a.AgentId == agentId, ct);
        if (agent is not null)
        {
            agent.LastSeenAt = nowUtc;
            if (agent.CurrentDispatchId == dispatchId) agent.CurrentDispatchId = null;
        }

        await _db.SaveChangesAsync(ct);
        return FinishOutcome.Recorded;
    }

    /// <summary>
    /// Moves every job of an agent group between the crontab and the dispatcher. UpdatedAt
    /// moves to now, so the dispatcher starts with the next slot and never repeats one that
    /// cron already ran. Moving back to cron expires the group's pending dispatches.
    /// </summary>
    public async Task<List<string>> SetGroupManagedByAsync(
        string group, string managedBy, string? changedBy, string? reason, DateTime nowUtc, CancellationToken ct)
    {
        var jobs = await _db.ScheduledJobs.Where(j => j.AgentGroup == group).ToListAsync(ct);
        var changed = new List<string>();
        foreach (var job in jobs.Where(j => j.ManagedBy != managedBy))
        {
            _db.ScheduledJobChanges.Add(new ScheduledJobChange
            {
                Id = Guid.NewGuid(),
                JobName = job.Name,
                ChangedAt = nowUtc,
                ChangedBy = changedBy,
                Reason = reason,
                OldJson = JsonSerializer.Serialize(new { managedBy = job.ManagedBy }),
                NewJson = JsonSerializer.Serialize(new { managedBy }),
            });
            job.ManagedBy = managedBy;
            job.UpdatedAt = nowUtc;
            job.UpdatedBy = changedBy;
            changed.Add(job.Name);
        }

        if (managedBy == ScheduledJobManagedBy.Cron)
        {
            var pending = await _db.JobDispatches
                .Where(d => d.AgentGroup == group && d.Status == JobDispatchStatus.Pending)
                .ToListAsync(ct);
            foreach (var d in pending)
            {
                d.Status = JobDispatchStatus.Expired;
                d.Note = "the group moved back to cron";
            }
        }

        await _db.SaveChangesAsync(ct);
        return changed;
    }

    public static DateTime? NewestSlot(string? cron, DateTime afterUtc, DateTime nowUtc)
    {
        DateTime? newest = null;
        var from = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        if (from >= to) return null;
        foreach (var expression in ScheduledJobRules.SplitCron(cron))
        {
            if (!JobNextRunCalculator.TryParseCron(expression, out var parsed)) continue;
            var last = parsed.GetOccurrences(from, to, TimeZoneInfo.Utc, fromInclusive: false, toInclusive: true)
                .LastOrDefault();
            if (last != default && (newest is null || last > newest)) newest = last;
        }
        return newest;
    }

    private async Task TouchAgentAsync(
        string group, string agentId, string? host, Guid? dispatchId, DateTime nowUtc, bool setDispatch, CancellationToken ct)
    {
        var agent = await _db.JobAgents.FirstOrDefaultAsync(a => a.AgentId == agentId, ct);
        if (agent is null)
        {
            agent = new JobAgent { AgentId = agentId, FirstSeenAt = nowUtc };
            _db.JobAgents.Add(agent);
        }
        agent.AgentGroup = group;
        agent.Host = host;
        agent.LastSeenAt = nowUtc;
        if (setDispatch) agent.CurrentDispatchId = dispatchId;
        await _db.SaveChangesAsync(ct);
    }

    private static DateTime Max(params DateTime[] values) =>
        DateTime.SpecifyKind(values.Max(), DateTimeKind.Utc);
}
