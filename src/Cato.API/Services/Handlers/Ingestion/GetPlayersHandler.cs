using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetPlayersHandler : IRequestHandler<GetPlayersQuery, PlayersDto>
{
    private readonly IGameDataService _gameDataService;

    public GetPlayersHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<PlayersDto> Handle(GetPlayersQuery request, CancellationToken ct)
        => _gameDataService.GetPlayersAsync(request, ct);
}
