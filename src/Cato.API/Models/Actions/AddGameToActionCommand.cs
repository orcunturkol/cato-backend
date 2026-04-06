using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record AddGameToActionCommand(
    Guid ActionId,
    Guid GameId,
    string? GameRole,
    string? Notes
) : IRequest<Result<GameActionDto>>;
