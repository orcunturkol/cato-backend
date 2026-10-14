using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetSalesDailyHandler : IRequestHandler<GetSalesDailyQuery, List<SalesDailyDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetSalesDailyHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<SalesDailyDto>> Handle(GetSalesDailyQuery request, CancellationToken ct)
        => _gameDataService.GetSalesDailyAsync(request, ct);
}

public class GetSalesCountriesHandler : IRequestHandler<GetSalesCountriesQuery, SalesCountriesDto>
{
    private readonly IGameDataService _gameDataService;

    public GetSalesCountriesHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<SalesCountriesDto> Handle(GetSalesCountriesQuery request, CancellationToken ct)
        => _gameDataService.GetSalesCountriesAsync(request, ct);
}

public class GetSalesSummaryHandler : IRequestHandler<GetSalesSummaryQuery, SalesSummaryDto>
{
    private readonly IGameDataService _gameDataService;

    public GetSalesSummaryHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<SalesSummaryDto> Handle(GetSalesSummaryQuery request, CancellationToken ct)
        => _gameDataService.GetSalesSummaryAsync(ct);
}
