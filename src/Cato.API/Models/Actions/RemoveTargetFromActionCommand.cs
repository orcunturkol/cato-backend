using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record RemoveTargetFromActionCommand(Guid ActionId, Guid TargetId) : IRequest<Result<bool>>;
