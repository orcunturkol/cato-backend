using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record CatalogGamesQuery(
    DateTime? CreatedAfter = null,
    IReadOnlyList<int>? AppIds = null,
    string? Search = null,
    string? Developer = null,
    string? Publisher = null,
    DateOnly? ReleasedAfter = null,
    DateOnly? ReleasedBefore = null,
    bool? HasTrailer = null,
    int Page = 1,
    int PageSize = 100
) : IRequest<PagedResult<GameDto>>;
