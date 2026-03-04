using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record ListGamesQuery(
    string? GameType = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<GameDto>>;
