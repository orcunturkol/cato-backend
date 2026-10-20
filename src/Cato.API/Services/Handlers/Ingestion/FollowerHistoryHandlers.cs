using Cato.API.Models.Ingestion;
using Cato.API.Services;
using Cato.API.Services.Dispatch;
using Cato.Infrastructure.Redis;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class RequestFollowerHistoryHandler : IRequestHandler<RequestFollowerHistoryCommand, FollowerHistoryState>
{
    /// <summary>The follower pass that drains only requested games.</summary>
    public const string RequestPassJob = "steamdb_followers_priority";

    private readonly IFollowerHistoryQueueService _queue;
    private readonly JobDispatchService _dispatch;
    private readonly TimeProvider _time;

    public RequestFollowerHistoryHandler(IFollowerHistoryQueueService queue, JobDispatchService dispatch, TimeProvider time)
    {
        _queue = queue;
        _dispatch = dispatch;
        _time = time;
    }

    public async Task<FollowerHistoryState> Handle(RequestFollowerHistoryCommand request, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var state = await _queue.RequestAsync(request.AppId, now, ct);
        // Once the steamdb agent runs the follower jobs, the request starts its pass at once.
        // Still on cron, or a pass already waiting or running: that pass picks the game up.
        await _dispatch.RequestRunAsync(RequestPassJob, $"follower history: app {request.AppId}", now.UtcDateTime, ct);
        return state;
    }
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
