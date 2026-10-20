using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetNavStatsHandler : IRequestHandler<GetNavStatsQuery, NavStatsDto>
{
    private readonly IGameDataService _gameDataService;

    public GetNavStatsHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<NavStatsDto> Handle(GetNavStatsQuery request, CancellationToken ct)
        => _gameDataService.GetNavStatsAsync(request, ct);
}
