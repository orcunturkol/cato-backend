namespace Cato.Domain.Entities;

/// <summary>
/// One job: when it runs, how long it may take, what runs it and when its failed items
/// alert (table scheduled_job). A collector job has a cron and an agent group; the crontab
/// or the job dispatcher starts it (<see cref="ManagedBy"/>), and only dispatcher-managed
/// ones can be edited, because the crontab ignores edits. A backend watcher
/// (<see cref="ScheduledJobManagedBy.Worker"/>) has an interval instead; it reads its row
/// before each cycle, and writes the row from its settings the first time.
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

    /// <summary>Watchers only: minutes from the end of one cycle to the start of the next.</summary>
    public int? IntervalMinutes { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Hard time limit; the agent kills the run after it.</summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>Alert when failed items / total items reaches this share (0.10 = 10%).</summary>
    public decimal AlertFailedRatio { get; set; } = 0.10m;

    /// <summary>Do not alert below this many failed items.</summary>
    public int AlertMinItems { get; set; } = 1;

    /// <summary>"cron" (the host crontab starts it), "dispatcher" or "worker" (a backend watcher).</summary>
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
    /// <summary>A backend watcher in the worker process; no agent and no dispatch.</summary>
    public const string Worker = "worker";
}

public static class JobAgentGroups
{
    public const string Light = "light";
    public const string Events = "events";
    public const string SteamDb = "steamdb";
    public const string Steamworks = "steamworks";

    public static readonly IReadOnlyList<string> All = [Light, Events, SteamDb, Steamworks];

    /// <summary>The group of backend watcher rows; not an agent, so not in <see cref="All"/>.</summary>
    public const string Worker = "worker";
}
