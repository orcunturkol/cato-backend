using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.SteamDb;

public record GetSteamDbRankingsQuery(
    string Source,
    DateOnly? Date = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50
) : IRequest<PagedResult<SteamDbRankingDto>>;
