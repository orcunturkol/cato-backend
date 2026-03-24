using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.AppHistory;

/// <summary>
/// Returns paginated changelist summaries for a game, ordered by most recent first.
/// </summary>
public record GetChangelistsQuery(
    Guid GameId,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<ChangelistSummaryDto>>;

public record ChangelistSummaryDto
{
    /// <summary>
    /// PICS changelist number
    /// </summary>
    /// <example>34467426</example>
    public long ChangeNumber { get; init; }

    /// <summary>
    /// When the change was detected
    /// </summary>
    public DateTime DetectedAt { get; init; }

    /// <summary>
    /// Number of individual key-value changes in this changelist
    /// </summary>
    /// <example>15</example>
    public int ChangeCount { get; init; }

    /// <summary>
    /// Top-level sections affected (e.g. common, depots, config)
    /// </summary>
    public List<string> SectionsAffected { get; init; } = [];
}
