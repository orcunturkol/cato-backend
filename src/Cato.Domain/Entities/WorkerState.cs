namespace Cato.Domain.Entities;

/// <summary>
/// Small values a background service needs again after a restart (table worker_state), such
/// as the PICS change numbers. Before 2026-10 they were files inside the container, so every
/// re-created container started PICS from change 0.
/// </summary>
public class WorkerState
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public static class WorkerStateKeys
{
    public const string PicsWatcherChangeNumber = "pics_watcher_change_number";
    public const string PicsHistoryChangeNumber = "pics_history_change_number";
    /// <summary>When the process that runs the backend watchers last started (ISO 8601, UTC).</summary>
    public const string WorkerStartedAt = "worker_started_at";
    /// <summary>The last app id the price watcher saved in its current cycle; 0 once the cycle is complete.</summary>
    public const string PriceWatcherLastAppId = "price_watcher_last_app_id";
}
