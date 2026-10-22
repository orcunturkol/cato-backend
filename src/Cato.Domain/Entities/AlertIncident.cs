namespace Cato.Domain.Entities;

/// <summary>
/// One alert (table alert_incident): a rule that fires for a job, an agent or a queue.
/// It opens once, sends one message per channel, and resolves at the next good state
/// with one more message. <see cref="OpenKey"/> is "job|rule" while the incident is
/// open and null after, so a unique index allows one open incident per job and rule.
/// </summary>
public class AlertIncident
{
    public Guid Id { get; set; }

    /// <summary>A job name ("ccu"), "agent:&lt;agent group&gt;" or "queue:&lt;queue name&gt;".</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>One of <see cref="AlertRules"/>.</summary>
    public string Rule { get; set; } = string.Empty;

    public string? OpenKey { get; set; }

    /// <summary>What is wrong now; updated while the incident stays open.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The job run that opened it or last kept it open, when a run did.</summary>
    public Guid? RunId { get; set; }

    public DateTime OpenedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Comma-separated channels that sent the "opened" message ("sns,discord").</summary>
    public string OpenSentTo { get; set; } = string.Empty;

    /// <summary>Comma-separated channels that sent the "resolved" message.</summary>
    public string ResolveSentTo { get; set; } = string.Empty;
}

public static class AlertRules
{
    /// <summary>The newest finished run failed or was lost.</summary>
    public const string RunFailed = "run_failed";
    /// <summary>Failed items / total items reached the job's threshold.</summary>
    public const string ItemsFailed = "items_failed";
    /// <summary>A dispatch was not claimed in time.</summary>
    public const string MissedRun = "missed_run";
    /// <summary>An agent stopped calling the API.</summary>
    public const string AgentOffline = "agent_offline";
    /// <summary>The dead-letter queue holds rejected batches.</summary>
    public const string DlqNotEmpty = "dlq_not_empty";
    /// <summary>A queue has messages and makes no progress.</summary>
    public const string QueueStuck = "queue_stuck";
    /// <summary>The newest data a job writes is older than its limit.</summary>
    public const string StaleData = "stale_data";

    public static string Title(string rule) => rule switch
    {
        RunFailed => "run failed",
        ItemsFailed => "too many failed items",
        MissedRun => "missed run",
        AgentOffline => "agent offline",
        DlqNotEmpty => "rejected batches in the DLQ",
        QueueStuck => "queue stuck",
        StaleData => "stale data",
        _ => rule,
    };
}
