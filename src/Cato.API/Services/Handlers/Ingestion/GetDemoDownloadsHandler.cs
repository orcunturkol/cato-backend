using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetDemoDownloadsHandler : IRequestHandler<GetDemoDownloadsQuery, DemoDownloadsDto>
{
    private readonly IGameDataService _gameDataService;

    public GetDemoDownloadsHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<DemoDownloadsDto> Handle(GetDemoDownloadsQuery request, CancellationToken ct)
        => _gameDataService.GetDemoDownloadsAsync(request, ct);
}
