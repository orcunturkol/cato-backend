using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetSteamworksWishlistDailyHandler : IRequestHandler<GetSteamworksWishlistDailyQuery, List<SteamworksWishlistDailyDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetSteamworksWishlistDailyHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<SteamworksWishlistDailyDto>> Handle(GetSteamworksWishlistDailyQuery request, CancellationToken ct)
        => _gameDataService.GetSteamworksWishlistDailyAsync(request, ct);
}
