using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class EnrichGameFromSteamHandler : IRequestHandler<EnrichGameFromSteamCommand, Result<GameDto>>
{
    private readonly IGameService _gameService;

    public EnrichGameFromSteamHandler(IGameService gameService) => _gameService = gameService;

    public Task<Result<GameDto>> Handle(EnrichGameFromSteamCommand request, CancellationToken ct)
        => _gameService.EnrichGameFromSteamAsync(request.Id, ct);
}
