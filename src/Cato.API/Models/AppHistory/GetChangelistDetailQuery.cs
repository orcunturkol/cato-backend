using MediatR;

namespace Cato.API.Models.AppHistory;

/// <summary>
/// Returns all change records for a specific changelist of a game.
/// </summary>
public record GetChangelistDetailQuery(
    Guid GameId,
    long ChangeNumber
) : IRequest<List<AppChangeRecordDto>>;

public record AppChangeRecordDto
{
    /// <summary>
    /// PICS changelist number
    /// </summary>
    /// <example>34467426</example>
    public long ChangeNumber { get; init; }

    /// <summary>
    /// Top-level section (common, depots, config, extended, ufs)
    /// </summary>
    /// <example>depots</example>
    public string Section { get; init; } = string.Empty;

    /// <summary>
    /// Dot-delimited key path (e.g. depots.731.manifests.public.gid)
    /// </summary>
    /// <example>depots.731.manifests.public.gid</example>
    public string KeyPath { get; init; } = string.Empty;

    /// <summary>
    /// Change action: Added, Modified, or Removed
    /// </summary>
    /// <example>Modified</example>
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// Previous value (null for Added)
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// New value (null for Removed)
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    /// When the change was detected
    /// </summary>
    public DateTime DetectedAt { get; init; }
}
