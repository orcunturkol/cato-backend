using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record UpdateActionCommand(
    Guid Id,
    string? ActionType,
    string? DecisionSource,
    string? Status,
    DateOnly? PlannedDate,
    DateOnly? ActionDate,
    DateOnly? CompletionDate,
    string? Description,
    decimal? BudgetUsd,
    decimal? ActualCostUsd,
    string? Notes,
    string? CreatedBy
) : IRequest<Result<MarketingActionDto>>;
