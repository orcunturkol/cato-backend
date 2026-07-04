namespace Cato.API.Services;

/// <summary>
/// Message contract published by Python collectors.
/// </summary>
public record IngestionMessage
{
    public string Source { get; init; } = string.Empty;
    /// <summary>Null for sources not keyed by a single app (e.g. steam_special_events).</summary>
    public int? AppId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public DateTime? CollectedAt { get; init; }
}
