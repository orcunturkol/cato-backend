namespace Cato.Domain.Entities;

/// <summary>
/// The schedule an external producer last published, one row per producer.
/// Backend watcher schedules are not stored; they come from the API's settings.
/// </summary>
public class JobScheduleSnapshot
{
    /// <summary>Producer that owns the schedule, e.g. "external-collector".</summary>
    public string Producer { get; set; } = string.Empty;

    /// <summary>Where the producer read the schedule from, e.g. "crontab".</summary>
    public string Source { get; set; } = string.Empty;

    public string? SourceHost { get; set; }

    /// <summary>JSON array of schedule entries (jsonb).</summary>
    public string EntriesJson { get; set; } = "[]";

    public DateTime PublishedAt { get; set; }
}
