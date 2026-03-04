using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record CreateGameCommand(
    int AppId,
    string? Name,
    string? GameType,
    string? DeveloperName,
    string? PublisherName
) : IRequest<Result<GameDto>>;
