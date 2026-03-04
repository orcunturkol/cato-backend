using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetTrafficDataHandler : IRequestHandler<GetTrafficDataQuery, List<TrafficDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetTrafficDataHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<TrafficDto>> Handle(GetTrafficDataQuery request, CancellationToken ct)
        => _gameDataService.GetTrafficDataAsync(request, ct);
}
