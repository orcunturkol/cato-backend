using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetSteamworksWishlistCountriesHandler : IRequestHandler<GetSteamworksWishlistCountriesQuery, SteamworksWishlistCountriesDto>
{
    private readonly IGameDataService _gameDataService;

    public GetSteamworksWishlistCountriesHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<SteamworksWishlistCountriesDto> Handle(GetSteamworksWishlistCountriesQuery request, CancellationToken ct)
        => _gameDataService.GetSteamworksWishlistCountriesAsync(request, ct);
}
