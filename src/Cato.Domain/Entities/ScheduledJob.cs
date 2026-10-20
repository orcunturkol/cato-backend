namespace Cato.Domain.Entities;

/// <summary>
/// One collector job: when it runs, how long it may take, which agent runs it and when its
/// failed items alert (table scheduled_job). Jobs move from the host crontab to the job
/// dispatcher one agent group at a time; <see cref="ManagedBy"/> says which one starts the
/// job today. Only dispatcher-managed jobs can be edited, because the crontab ignores edits.
/// </summary>
public class ScheduledJob
{
    /// <summary>Job name, as in job_run (e.g. "ccu").</summary>
    public string Name { get; set; } = string.Empty;

    public string? Label { get; set; }

    /// <summary>The agent that runs it: light, events, steamdb or steamworks.</summary>
    public string AgentGroup { get; set; } = string.Empty;

    /// <summary>Python module the agent starts, e.g. "orchestrators.run_ccu".</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Extra command-line arguments, e.g. "--max-games 40".</summary>
    public string Args { get; set; } = string.Empty;

    /// <summary>UTC cron; several expressions are joined by " | ". Null: run on request only.</summary>
    public string? Cron { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Hard time limit; the agent kills the run after it.</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>Alert when failed items / total items reaches this share (0.10 = 10%).</summary>
    public decimal AlertFailedRatio { get; set; } = 0.10m;

    /// <summary>Do not alert below this many failed items.</summary>
    public int AlertMinItems { get; set; } = 1;

    /// <summary>"cron" (the host crontab starts it) or "dispatcher".</summary>
    public string ManagedBy { get; set; } = ScheduledJobManagedBy.Cron;

    public DateTime UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}

/// <summary>One edit of a <see cref="ScheduledJob"/>: who, when, why, and the values before and after.</summary>
public class ScheduledJobChange
{
    public Guid Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; }

    public string? ChangedBy { get; set; }

    public string? Reason { get; set; }

    /// <summary>JSON of the editable fields before the change (jsonb).</summary>
    public string OldJson { get; set; } = "{}";

    /// <summary>JSON of the editable fields after the change (jsonb).</summary>
    public string NewJson { get; set; } = "{}";
}

public static class ScheduledJobManagedBy
{
    public const string Cron = "cron";
    public const string Dispatcher = "dispatcher";
}

public static class JobAgentGroups
{
    public const string Light = "light";
    public const string Events = "events";
    public const string SteamDb = "steamdb";
    public const string Steamworks = "steamworks";

    public static readonly IReadOnlyList<string> All = [Light, Events, SteamDb, Steamworks];
}
