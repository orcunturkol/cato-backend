using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetAchievementsHandler : IRequestHandler<GetAchievementsQuery, AchievementsDto>
{
    private readonly IGameDataService _gameDataService;

    public GetAchievementsHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<AchievementsDto> Handle(GetAchievementsQuery request, CancellationToken ct)
        => _gameDataService.GetAchievementsAsync(request, ct);
}

public class GetGameNewsHandler : IRequestHandler<GetGameNewsQuery, List<GameNewsDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetGameNewsHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<GameNewsDto>> Handle(GetGameNewsQuery request, CancellationToken ct)
        => _gameDataService.GetGameNewsAsync(request, ct);
}
