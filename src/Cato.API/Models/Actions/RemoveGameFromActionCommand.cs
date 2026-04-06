using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record RemoveGameFromActionCommand(Guid ActionId, Guid GameId) : IRequest<Result<bool>>;
