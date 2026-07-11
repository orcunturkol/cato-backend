using System.Text.Json;

namespace Cato.API.Services;

/// <summary>
/// Message contract published by Python collectors.
/// </summary>
public record IngestionMessage
{
    public string Source { get; init; } = string.Empty;
    /// <summary>Null for sources not keyed by a single app (e.g. steam_special_events).</summary>
    public int? AppId { get; init; }
    /// <summary>Absolute path to a file the API reads (legacy file-based sources).</summary>
    public string FilePath { get; init; } = string.Empty;
    /// <summary>Inline JSON payload; used instead of FilePath so no shared disk is needed.</summary>
    public JsonElement? Data { get; init; }
    public DateTime? CollectedAt { get; init; }
}
