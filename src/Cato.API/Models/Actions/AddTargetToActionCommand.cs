using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record AddTargetToActionCommand(
    Guid ActionId,
    Guid TargetId,
    string? Status,
    DateOnly? OutreachDate,
    string? Notes
) : IRequest<Result<ActionTargetDto>>;
