using Cato.API.Models.SteamDb;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.SteamDb;

public class GetAvailableDatesHandler : IRequestHandler<GetAvailableDatesQuery, List<DateOnly>>
{
    private readonly IGameDataService _gameDataService;

    public GetAvailableDatesHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<DateOnly>> Handle(GetAvailableDatesQuery request, CancellationToken ct)
        => _gameDataService.GetAvailableDatesAsync(request, ct);
}
