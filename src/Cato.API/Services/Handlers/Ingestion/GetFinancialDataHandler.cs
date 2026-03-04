using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetFinancialDataHandler : IRequestHandler<GetFinancialDataQuery, List<FinancialDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetFinancialDataHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<FinancialDto>> Handle(GetFinancialDataQuery request, CancellationToken ct)
        => _gameDataService.GetFinancialDataAsync(request, ct);
}
