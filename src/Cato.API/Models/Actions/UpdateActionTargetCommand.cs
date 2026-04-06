using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record UpdateActionTargetCommand(
    Guid ActionId,
    Guid TargetId,
    string? Status,
    DateOnly? OutreachDate,
    DateOnly? ResponseDate,
    string? DeliverableUrl,
    int? Views,
    int? Engagement,
    decimal? CostUsd,
    string? Notes
) : IRequest<Result<ActionTargetDto>>;
