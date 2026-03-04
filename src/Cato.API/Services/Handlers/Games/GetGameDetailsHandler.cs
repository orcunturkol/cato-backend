using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class GetGameDetailsHandler : IRequestHandler<GetGameDetailsQuery, Result<GameDto>>
{
    private readonly IGameService _gameService;

    public GetGameDetailsHandler(IGameService gameService) => _gameService = gameService;

    public Task<Result<GameDto>> Handle(GetGameDetailsQuery request, CancellationToken ct)
        => _gameService.GetGameDetailsAsync(request.Id, ct);
}
