using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetStoreTrafficPeriodsHandler : IRequestHandler<GetStoreTrafficPeriodsQuery, List<StoreTrafficPeriodDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetStoreTrafficPeriodsHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<StoreTrafficPeriodDto>> Handle(GetStoreTrafficPeriodsQuery request, CancellationToken ct)
        => _gameDataService.GetStoreTrafficPeriodsAsync(request, ct);
}

public class GetStoreTrafficBreakdownHandler : IRequestHandler<GetStoreTrafficBreakdownQuery, StoreTrafficBreakdownDto>
{
    private readonly IGameDataService _gameDataService;

    public GetStoreTrafficBreakdownHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<StoreTrafficBreakdownDto> Handle(GetStoreTrafficBreakdownQuery request, CancellationToken ct)
        => _gameDataService.GetStoreTrafficBreakdownAsync(request, ct);
}
