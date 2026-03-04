using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetCcuHistoryHandler : IRequestHandler<GetCcuHistoryQuery, List<CcuDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetCcuHistoryHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<CcuDto>> Handle(GetCcuHistoryQuery request, CancellationToken ct)
        => _gameDataService.GetCcuHistoryAsync(request, ct);
}
