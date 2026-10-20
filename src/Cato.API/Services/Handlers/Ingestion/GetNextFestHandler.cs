using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetNextFestHandler : IRequestHandler<GetNextFestQuery, NextFestDto>
{
    private readonly IGameDataService _gameDataService;

    public GetNextFestHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<NextFestDto> Handle(GetNextFestQuery request, CancellationToken ct) => _gameDataService.GetNextFestAsync(ct);
}
