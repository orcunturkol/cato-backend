using Cato.API.DTOs;
using Cato.API.Models.Actions;

namespace Cato.API.Services;

public interface IMarketingActionService
{
    Task<Result<MarketingActionDto>> CreateAsync(CreateActionCommand request, CancellationToken ct = default);
    Task<Result<MarketingActionDto>> UpdateAsync(UpdateActionCommand request, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result<MarketingActionDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<MarketingActionSummaryDto>> ListAsync(ListActionsQuery request, CancellationToken ct = default);
    Task<Result<GameActionDto>> AddGameAsync(AddGameToActionCommand request, CancellationToken ct = default);
    Task<Result<bool>> RemoveGameAsync(Guid actionId, Guid gameId, CancellationToken ct = default);
    Task<Result<ActionTargetDto>> AddTargetAsync(AddTargetToActionCommand request, CancellationToken ct = default);
    Task<Result<bool>> RemoveTargetAsync(Guid actionId, Guid targetId, CancellationToken ct = default);
    Task<Result<ActionTargetDto>> UpdateTargetAsync(UpdateActionTargetCommand request, CancellationToken ct = default);
    Task<Result<ActionImpactDto>> UpsertImpactAsync(UpsertActionImpactCommand request, CancellationToken ct = default);
    Task<Result<ActionImpactDto>> GetImpactAsync(Guid actionId, CancellationToken ct = default);
}
