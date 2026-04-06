using Cato.API.DTOs;
using Cato.API.Models.Actions;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services;

public class MarketingActionService : IMarketingActionService
{
    private readonly CatoDbContext _db;

    public MarketingActionService(CatoDbContext db)
    {
        _db = db;
    }

    public async Task<Result<MarketingActionDto>> CreateAsync(CreateActionCommand request, CancellationToken ct = default)
    {
        var action = new MarketingAction
        {
            Id = Guid.NewGuid(),
            ActionType = request.ActionType,
            DecisionSource = request.DecisionSource ?? "Manual",
            Status = request.Status ?? "Planned",
            PlannedDate = request.PlannedDate,
            ActionDate = request.ActionDate,
            Description = request.Description,
            BudgetUsd = request.BudgetUsd,
            Notes = request.Notes,
            CreatedBy = request.CreatedBy
        };

        _db.MarketingActions.Add(action);

        if (request.Games is { Count: > 0 })
        {
            foreach (var g in request.Games)
            {
                _db.GameActions.Add(new GameAction
                {
                    Id = Guid.NewGuid(),
                    ActionId = action.Id,
                    GameId = g.GameId,
                    GameRole = g.GameRole ?? "Primary",
                    Notes = g.Notes
                });
            }
        }

        if (request.TargetIds is { Count: > 0 })
        {
            foreach (var targetId in request.TargetIds)
            {
                _db.ActionTargets.Add(new ActionTarget
                {
                    Id = Guid.NewGuid(),
                    ActionId = action.Id,
                    TargetId = targetId,
                    Status = "Planned"
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return Result<MarketingActionDto>.Success(await LoadFullAction(action.Id, ct));
    }

    public async Task<Result<MarketingActionDto>> UpdateAsync(UpdateActionCommand request, CancellationToken ct = default)
    {
        var action = await _db.MarketingActions.FirstOrDefaultAsync(a => a.Id == request.Id, ct);
        if (action is null)
            return Result<MarketingActionDto>.Failure($"Action with Id {request.Id} not found.");

        if (request.ActionType is not null) action.ActionType = request.ActionType;
        if (request.DecisionSource is not null) action.DecisionSource = request.DecisionSource;
        if (request.Status is not null) action.Status = request.Status;
        if (request.PlannedDate.HasValue) action.PlannedDate = request.PlannedDate.Value;
        if (request.ActionDate.HasValue) action.ActionDate = request.ActionDate.Value;
        if (request.CompletionDate.HasValue) action.CompletionDate = request.CompletionDate.Value;
        if (request.Description is not null) action.Description = request.Description;
        if (request.BudgetUsd.HasValue) action.BudgetUsd = request.BudgetUsd.Value;
        if (request.ActualCostUsd.HasValue) action.ActualCostUsd = request.ActualCostUsd.Value;
        if (request.Notes is not null) action.Notes = request.Notes;
        if (request.CreatedBy is not null) action.CreatedBy = request.CreatedBy;

        await _db.SaveChangesAsync(ct);
        return Result<MarketingActionDto>.Success(await LoadFullAction(action.Id, ct));
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var action = await _db.MarketingActions.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (action is null)
            return Result<bool>.Failure($"Action with Id {id} not found.");

        _db.MarketingActions.Remove(action);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<MarketingActionDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var action = await _db.MarketingActions
            .AsNoTracking()
            .Include(a => a.GameActions).ThenInclude(ga => ga.Game)
            .Include(a => a.ActionTargets).ThenInclude(at => at.Target)
            .Include(a => a.Impact)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (action is null)
            return Result<MarketingActionDto>.Failure($"Action with Id {id} not found.");

        return Result<MarketingActionDto>.Success(action.ToDto());
    }

    public async Task<PagedResult<MarketingActionSummaryDto>> ListAsync(ListActionsQuery request, CancellationToken ct = default)
    {
        var query = _db.MarketingActions
            .AsNoTracking()
            .Include(a => a.GameActions)
            .Include(a => a.ActionTargets)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.ActionType))
            query = query.Where(a => a.ActionType == request.ActionType);

        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(a => a.Status == request.Status);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(a => EF.Functions.ILike(a.Description, $"%{request.Search}%"));

        var totalCount = await query.CountAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<MarketingActionSummaryDto>
        {
            Items = items.Select(a => a.ToSummaryDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<Result<GameActionDto>> AddGameAsync(AddGameToActionCommand request, CancellationToken ct = default)
    {
        var actionExists = await _db.MarketingActions.AnyAsync(a => a.Id == request.ActionId, ct);
        if (!actionExists)
            return Result<GameActionDto>.Failure($"Action with Id {request.ActionId} not found.");

        var gameExists = await _db.Games.AnyAsync(g => g.Id == request.GameId, ct);
        if (!gameExists)
            return Result<GameActionDto>.Failure($"Game with Id {request.GameId} not found.");

        var alreadyLinked = await _db.GameActions
            .AnyAsync(ga => ga.ActionId == request.ActionId && ga.GameId == request.GameId, ct);
        if (alreadyLinked)
            return Result<GameActionDto>.Failure("This game is already linked to this action.");

        var gameAction = new GameAction
        {
            Id = Guid.NewGuid(),
            ActionId = request.ActionId,
            GameId = request.GameId,
            GameRole = request.GameRole ?? "Primary",
            Notes = request.Notes
        };

        _db.GameActions.Add(gameAction);
        await _db.SaveChangesAsync(ct);

        var loaded = await _db.GameActions
            .AsNoTracking()
            .Include(ga => ga.Game)
            .FirstAsync(ga => ga.Id == gameAction.Id, ct);

        return Result<GameActionDto>.Success(loaded.ToDto());
    }

    public async Task<Result<bool>> RemoveGameAsync(Guid actionId, Guid gameId, CancellationToken ct = default)
    {
        var gameAction = await _db.GameActions
            .FirstOrDefaultAsync(ga => ga.ActionId == actionId && ga.GameId == gameId, ct);
        if (gameAction is null)
            return Result<bool>.Failure("Game-action link not found.");

        _db.GameActions.Remove(gameAction);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<ActionTargetDto>> AddTargetAsync(AddTargetToActionCommand request, CancellationToken ct = default)
    {
        var actionExists = await _db.MarketingActions.AnyAsync(a => a.Id == request.ActionId, ct);
        if (!actionExists)
            return Result<ActionTargetDto>.Failure($"Action with Id {request.ActionId} not found.");

        var targetExists = await _db.MarketingTargets.AnyAsync(t => t.Id == request.TargetId, ct);
        if (!targetExists)
            return Result<ActionTargetDto>.Failure($"Marketing target with Id {request.TargetId} not found.");

        var alreadyLinked = await _db.ActionTargets
            .AnyAsync(at => at.ActionId == request.ActionId && at.TargetId == request.TargetId, ct);
        if (alreadyLinked)
            return Result<ActionTargetDto>.Failure("This target is already linked to this action.");

        var actionTarget = new ActionTarget
        {
            Id = Guid.NewGuid(),
            ActionId = request.ActionId,
            TargetId = request.TargetId,
            Status = request.Status ?? "Planned",
            OutreachDate = request.OutreachDate,
            Notes = request.Notes
        };

        _db.ActionTargets.Add(actionTarget);
        await _db.SaveChangesAsync(ct);

        var loaded = await _db.ActionTargets
            .AsNoTracking()
            .Include(at => at.Target)
            .FirstAsync(at => at.Id == actionTarget.Id, ct);

        return Result<ActionTargetDto>.Success(loaded.ToDto());
    }

    public async Task<Result<bool>> RemoveTargetAsync(Guid actionId, Guid targetId, CancellationToken ct = default)
    {
        var actionTarget = await _db.ActionTargets
            .FirstOrDefaultAsync(at => at.ActionId == actionId && at.TargetId == targetId, ct);
        if (actionTarget is null)
            return Result<bool>.Failure("Action-target link not found.");

        _db.ActionTargets.Remove(actionTarget);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<ActionTargetDto>> UpdateTargetAsync(UpdateActionTargetCommand request, CancellationToken ct = default)
    {
        var actionTarget = await _db.ActionTargets
            .Include(at => at.Target)
            .FirstOrDefaultAsync(at => at.ActionId == request.ActionId && at.TargetId == request.TargetId, ct);
        if (actionTarget is null)
            return Result<ActionTargetDto>.Failure("Action-target link not found.");

        if (request.Status is not null) actionTarget.Status = request.Status;
        if (request.OutreachDate.HasValue) actionTarget.OutreachDate = request.OutreachDate.Value;
        if (request.ResponseDate.HasValue) actionTarget.ResponseDate = request.ResponseDate.Value;
        if (request.DeliverableUrl is not null) actionTarget.DeliverableUrl = request.DeliverableUrl;
        if (request.Views.HasValue) actionTarget.Views = request.Views.Value;
        if (request.Engagement.HasValue) actionTarget.Engagement = request.Engagement.Value;
        if (request.CostUsd.HasValue) actionTarget.CostUsd = request.CostUsd.Value;
        if (request.Notes is not null) actionTarget.Notes = request.Notes;

        await _db.SaveChangesAsync(ct);
        return Result<ActionTargetDto>.Success(actionTarget.ToDto());
    }

    public async Task<Result<ActionImpactDto>> UpsertImpactAsync(UpsertActionImpactCommand request, CancellationToken ct = default)
    {
        var actionExists = await _db.MarketingActions.AnyAsync(a => a.Id == request.ActionId, ct);
        if (!actionExists)
            return Result<ActionImpactDto>.Failure($"Action with Id {request.ActionId} not found.");

        var impact = await _db.ActionImpacts.FirstOrDefaultAsync(ai => ai.ActionId == request.ActionId, ct);

        if (impact is null)
        {
            impact = new ActionImpact { Id = Guid.NewGuid(), ActionId = request.ActionId };
            _db.ActionImpacts.Add(impact);
        }

        impact.MeasurementStart = request.MeasurementStart;
        impact.MeasurementEnd = request.MeasurementEnd;
        impact.BaselineStart = request.BaselineStart;
        impact.BaselineEnd = request.BaselineEnd;
        impact.BaselineWishlistAdds = request.BaselineWishlistAdds;
        impact.ResultWishlistAdds = request.ResultWishlistAdds;
        impact.WishlistChange = request.WishlistChange;
        impact.WishlistChangePercent = request.WishlistChangePercent;
        impact.BaselineSalesUnits = request.BaselineSalesUnits;
        impact.ResultSalesUnits = request.ResultSalesUnits;
        impact.SalesUnitsChange = request.SalesUnitsChange;
        impact.SalesChangePercent = request.SalesChangePercent;
        impact.BaselineRevenueUsd = request.BaselineRevenueUsd;
        impact.ResultRevenueUsd = request.ResultRevenueUsd;
        impact.RevenueChangeUsd = request.RevenueChangeUsd;
        impact.RevenueChangePercent = request.RevenueChangePercent;
        impact.BaselineTraffic = request.BaselineTraffic;
        impact.ResultTraffic = request.ResultTraffic;
        impact.TrafficChange = request.TrafficChange;
        impact.TrafficChangePercent = request.TrafficChangePercent;
        impact.BaselineConversionRate = request.BaselineConversionRate;
        impact.ResultConversionRate = request.ResultConversionRate;
        impact.ConversionRateChange = request.ConversionRateChange;
        impact.TotalCostUsd = request.TotalCostUsd;
        impact.Roi = request.Roi;
        impact.Notes = request.Notes;
        impact.CalculatedAt = DateTime.UtcNow;
        impact.CalculatedBy = request.CalculatedBy;

        await _db.SaveChangesAsync(ct);
        return Result<ActionImpactDto>.Success(impact.ToDto());
    }

    public async Task<Result<ActionImpactDto>> GetImpactAsync(Guid actionId, CancellationToken ct = default)
    {
        var impact = await _db.ActionImpacts
            .AsNoTracking()
            .FirstOrDefaultAsync(ai => ai.ActionId == actionId, ct);

        if (impact is null)
            return Result<ActionImpactDto>.Failure($"No impact data found for action {actionId}.");

        return Result<ActionImpactDto>.Success(impact.ToDto());
    }

    private async Task<MarketingActionDto> LoadFullAction(Guid id, CancellationToken ct)
    {
        var action = await _db.MarketingActions
            .AsNoTracking()
            .Include(a => a.GameActions).ThenInclude(ga => ga.Game)
            .Include(a => a.ActionTargets).ThenInclude(at => at.Target)
            .Include(a => a.Impact)
            .FirstAsync(a => a.Id == id, ct);

        return action.ToDto();
    }
}
