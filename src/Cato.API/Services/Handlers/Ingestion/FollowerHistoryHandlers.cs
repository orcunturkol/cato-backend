using Cato.API.Models.Ingestion;
using Cato.API.Services;
using Cato.Infrastructure.Redis;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class RequestFollowerHistoryHandler : IRequestHandler<RequestFollowerHistoryCommand, FollowerHistoryState>
{
    private readonly IFollowerHistoryQueueService _queue;

    public RequestFollowerHistoryHandler(IFollowerHistoryQueueService queue) => _queue = queue;

    public Task<FollowerHistoryState> Handle(RequestFollowerHistoryCommand request, CancellationToken ct)
        => _queue.RequestAsync(request.AppId, DateTimeOffset.UtcNow, ct);
}

public class GetFollowerHistoryStatusHandler : IRequestHandler<GetFollowerHistoryStatusQuery, FollowerHistoryState>
{
    private readonly IFollowerHistoryQueueService _queue;

    public GetFollowerHistoryStatusHandler(IFollowerHistoryQueueService queue) => _queue = queue;

    public Task<FollowerHistoryState> Handle(GetFollowerHistoryStatusQuery request, CancellationToken ct)
        => _queue.GetStateAsync(request.AppId, ct);
}

public class GetFollowerSeriesHandler : IRequestHandler<GetFollowerSeriesQuery, List<FollowerPointDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetFollowerSeriesHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<FollowerPointDto>> Handle(GetFollowerSeriesQuery request, CancellationToken ct)
        => _gameDataService.GetFollowerSeriesAsync(request, ct);
}
