namespace Cato.Domain.Entities;

/// <summary>
/// One run the job dispatcher handed to an agent group (table job_dispatch): a cron slot
/// of a dispatcher-managed <see cref="ScheduledJob"/>, or a "run now" request. Agents claim
/// the oldest pending dispatch of their group, run it, and report its exit code.
/// (JobName, ScheduledAt) is unique, so a restart never dispatches one slot twice.
/// </summary>
public class JobDispatch
{
    public Guid Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    public string AgentGroup { get; set; } = string.Empty;

    /// <summary>The cron slot, or the request time for a manual run (UTC).</summary>
    public DateTime ScheduledAt { get; set; }

    /// <summary>"scheduled" or "manual".</summary>
    public string Kind { get; set; } = JobDispatchKind.Scheduled;

    public string? RequestedBy { get; set; }

    /// <summary>pending, claimed, finished, skipped, expired or lost.</summary>
    public string Status { get; set; } = JobDispatchStatus.Pending;

    public string? ClaimedBy { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int? ExitCode { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// An agent process as the API last heard from it (table job_agent). Every claim and
/// heartbeat refreshes <see cref="LastSeenAt"/>; a silent agent is what the
/// "agent offline" alert looks for.
/// </summary>
public class JobAgent
{
    /// <summary>"&lt;group&gt;@&lt;host&gt;", e.g. "steamdb@ip-172-31-39-113".</summary>
    public string AgentId { get; set; } = string.Empty;

    public string AgentGroup { get; set; } = string.Empty;

    public string? Host { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    /// <summary>The dispatch the agent runs now, if any.</summary>
    public Guid? CurrentDispatchId { get; set; }
}

public static class JobDispatchKind
{
    public const string Scheduled = "scheduled";
    public const string Manual = "manual";
}

public static class JobDispatchStatus
{
    public const string Pending = "pending";
    public const string Claimed = "claimed";
    public const string Finished = "finished";
    public const string Skipped = "skipped";
    public const string Expired = "expired";
    public const string Lost = "lost";
}
