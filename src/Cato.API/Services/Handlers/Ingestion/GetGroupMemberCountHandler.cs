using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetGroupMemberCountHandler : IRequestHandler<GetGroupMemberCountQuery, List<GroupMemberCountDto>>
{
    private readonly IGameDataService _gameDataService;

    public GetGroupMemberCountHandler(IGameDataService gameDataService) => _gameDataService = gameDataService;

    public Task<List<GroupMemberCountDto>> Handle(GetGroupMemberCountQuery request, CancellationToken ct)
        => _gameDataService.GetGroupMemberCountAsync(request, ct);
}
