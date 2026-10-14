using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetSteamworksWishlistSummaryHandler : IRequestHandler<GetSteamworksWishlistSummaryQuery, SteamworksWishlistSummaryDto>
{
    private readonly IGameDataService _gameDataService;

    public GetSteamworksWishlistSummaryHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<SteamworksWishlistSummaryDto> Handle(GetSteamworksWishlistSummaryQuery request, CancellationToken ct)
        => _gameDataService.GetSteamworksWishlistSummaryAsync(ct);
}
