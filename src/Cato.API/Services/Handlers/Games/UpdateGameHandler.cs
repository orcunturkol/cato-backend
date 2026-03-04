using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class UpdateGameHandler : IRequestHandler<UpdateGameCommand, Result<GameDto>>
{
    private readonly IGameService _gameService;

    public UpdateGameHandler(IGameService gameService) => _gameService = gameService;

    public Task<Result<GameDto>> Handle(UpdateGameCommand request, CancellationToken ct)
        => _gameService.UpdateGameAsync(request, ct);
}
