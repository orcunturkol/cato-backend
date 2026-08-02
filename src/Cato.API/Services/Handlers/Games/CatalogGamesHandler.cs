using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class CatalogGamesHandler : IRequestHandler<CatalogGamesQuery, PagedResult<GameDto>>
{
    private readonly IGameService _gameService;

    public CatalogGamesHandler(IGameService gameService) => _gameService = gameService;

    public Task<PagedResult<GameDto>> Handle(CatalogGamesQuery request, CancellationToken ct)
        => _gameService.CatalogGamesAsync(request, ct);
}
