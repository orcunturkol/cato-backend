namespace Cato.Domain.Entities;

/// <summary>
/// One execution of a tracked job — a .NET background watcher cycle (e.g. the
/// player-achievement fetch) or an external orchestrator run reported by the
/// catoptric-data-collector. Generalizes <see cref="IngestionLog"/> across every
/// job in the system so run history is queryable (what ran, when, did it succeed,
/// how long, with what counters). Per-job counters live in <see cref="MetricsJson"/>
/// (jsonb) because each job emits a different set.
/// </summary>
public class JobRun
{
    public Guid Id { get; set; }

    /// <summary>Stable job identifier, e.g. "PlayerAchievementWatcher", "steamdb".</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>Which service produced the run — see <see cref="JobRunProducer"/>.</summary>
    public string Producer { get; set; } = JobRunProducer.CatoBackend;

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>Wall-clock duration, set on completion.</summary>
    public long? DurationMs { get; set; }

    /// <summary>One of <see cref="JobRunStatus"/>.</summary>
    public string Status { get; set; } = JobRunStatus.Running;

    /// <summary>Per-job counter bag, serialized JSON object (jsonb). Null while running.</summary>
    public string? MetricsJson { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Status values for <see cref="JobRun.Status"/> (string-const convention, like <see cref="AchievementFetchStatus"/>).</summary>
public static class JobRunStatus
{
    /// <summary>Started, not yet finished.</summary>
    public const string Running = "Running";

    /// <summary>Finished with no failures.</summary>
    public const string Succeeded = "Succeeded";

    /// <summary>Finished, but some items failed / were quarantined (still produced useful output).</summary>
    public const string PartialSuccess = "PartialSuccess";

    /// <summary>Threw / aborted before completing.</summary>
    public const string Failed = "Failed";

    /// <summary>True when the run did not complete cleanly (used for alerting).</summary>
    public static bool IsFailure(string status) => status == Failed;
}

/// <summary>Known <see cref="JobRun.Producer"/> values.</summary>
public static class JobRunProducer
{
    public const string CatoBackend = "cato-backend";
    public const string ExternalCollector = "external-collector";
}
