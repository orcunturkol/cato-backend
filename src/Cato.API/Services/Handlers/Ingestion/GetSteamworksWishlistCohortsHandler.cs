using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetSteamworksWishlistCohortsHandler : IRequestHandler<GetSteamworksWishlistCohortsQuery, List<SteamworksWishlistCohortDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetSteamworksWishlistCohortsHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<SteamworksWishlistCohortDto>> Handle(GetSteamworksWishlistCohortsQuery request, CancellationToken ct)
        => _gameDataService.GetSteamworksWishlistCohortsAsync(request, ct);
}
