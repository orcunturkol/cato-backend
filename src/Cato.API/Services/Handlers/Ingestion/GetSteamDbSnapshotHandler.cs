using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetSteamDbSnapshotHandler : IRequestHandler<GetSteamDbSnapshotQuery, List<SteamDbSnapshotDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetSteamDbSnapshotHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<SteamDbSnapshotDto>> Handle(GetSteamDbSnapshotQuery request, CancellationToken ct)
        => _gameDataService.GetSteamDbSnapshotsAsync(request, ct);
}
