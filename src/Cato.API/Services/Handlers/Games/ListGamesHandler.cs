using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class ListGamesHandler : IRequestHandler<ListGamesQuery, PagedResult<GameDto>>
{
    private readonly IGameService _gameService;

    public ListGamesHandler(IGameService gameService) => _gameService = gameService;

    public Task<PagedResult<GameDto>> Handle(ListGamesQuery request, CancellationToken ct)
        => _gameService.ListGamesAsync(request, ct);
}
