using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class DeleteGameHandler : IRequestHandler<DeleteGameCommand, Result<bool>>
{
    private readonly IGameService _gameService;

    public DeleteGameHandler(IGameService gameService) => _gameService = gameService;

    public Task<Result<bool>> Handle(DeleteGameCommand request, CancellationToken ct)
        => _gameService.DeleteGameAsync(request.Id, ct);
}
