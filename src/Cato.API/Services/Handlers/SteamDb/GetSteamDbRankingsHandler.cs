using Cato.API.DTOs;
using Cato.API.Models.SteamDb;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.SteamDb;

public class GetSteamDbRankingsHandler : IRequestHandler<GetSteamDbRankingsQuery, PagedResult<SteamDbRankingDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetSteamDbRankingsHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<PagedResult<SteamDbRankingDto>> Handle(GetSteamDbRankingsQuery request, CancellationToken ct)
        => _gameDataService.GetSteamDbRankingsAsync(request, ct);
}
