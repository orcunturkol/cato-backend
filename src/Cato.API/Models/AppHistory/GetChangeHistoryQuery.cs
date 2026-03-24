using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.AppHistory;

/// <summary>
/// Returns paginated change records for a game with optional section filter.
/// </summary>
public record GetChangeHistoryQuery(
    Guid GameId,
    string? Section = null,
    int Page = 1,
    int PageSize = 50
) : IRequest<PagedResult<AppChangeRecordDto>>;
