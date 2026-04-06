using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record GameActionInput(Guid GameId, string? GameRole, string? Notes);

public record CreateActionCommand(
    string ActionType,
    string? DecisionSource,
    string? Status,
    DateOnly? PlannedDate,
    DateOnly? ActionDate,
    string Description,
    decimal? BudgetUsd,
    string? Notes,
    string? CreatedBy,
    List<GameActionInput>? Games = null,
    List<Guid>? TargetIds = null
) : IRequest<Result<MarketingActionDto>>;
