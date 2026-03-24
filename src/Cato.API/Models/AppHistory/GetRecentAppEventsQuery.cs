using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.AppHistory;

public record GetRecentAppEventsQuery(
    string? GameType = null,
    int Page = 1,
    int PageSize = 50
) : IRequest<PagedResult<RecentAppEventDto>>;

public record RecentAppEventDto
{
    public long ChangeNumber { get; init; }
    public int AppId { get; init; }
    public Guid? GameId { get; init; }
    public string GameName { get; init; } = string.Empty;
    public string? HeaderImageUrl { get; init; }
    public string GameType { get; init; } = string.Empty;

    /// <summary>
    /// Derived event type: "New", "NewOnStore", "Renamed", "RemovedFromStore", "Changed"
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable detail, e.g. the new name, old→new name, or section summary.
    /// </summary>
    public string EventDetail { get; init; } = string.Empty;

    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public int ChangeCount { get; init; }
    public DateTime DetectedAt { get; init; }
}
