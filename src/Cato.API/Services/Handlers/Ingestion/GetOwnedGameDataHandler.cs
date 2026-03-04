using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetOwnedGameDataHandler : IRequestHandler<GetOwnedGameDataQuery, List<OwnedGameDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetOwnedGameDataHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<OwnedGameDto>> Handle(GetOwnedGameDataQuery request, CancellationToken ct)
        => _gameDataService.GetOwnedGameDataAsync(request, ct);
}
