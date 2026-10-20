using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetBundlesHandler : IRequestHandler<GetBundlesQuery, BundlesDto>
{
    private readonly IGameDataService _gameDataService;

    public GetBundlesHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<BundlesDto> Handle(GetBundlesQuery request, CancellationToken ct)
        => _gameDataService.GetBundlesAsync(request, ct);
}

public class GetBundleHandler : IRequestHandler<GetBundleQuery, BundleDetailDto>
{
    private readonly IGameDataService _gameDataService;

    public GetBundleHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<BundleDetailDto> Handle(GetBundleQuery request, CancellationToken ct)
        => _gameDataService.GetBundleAsync(request, ct);
}
