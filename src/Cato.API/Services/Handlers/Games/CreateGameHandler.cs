using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class CreateGameHandler : IRequestHandler<CreateGameCommand, Result<GameDto>>
{
    private readonly IGameService _gameService;

    public CreateGameHandler(IGameService gameService) => _gameService = gameService;

    public Task<Result<GameDto>> Handle(CreateGameCommand request, CancellationToken ct)
        => _gameService.CreateGameAsync(request, ct);
}
