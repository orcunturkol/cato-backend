using Cato.API.DTOs;
using Cato.API.Models.Actions;
using Cato.API.Models.MarketingTargets;

namespace Cato.API.Services;

public interface IMarketingTargetService
{
    Task<Result<MarketingTargetDto>> CreateAsync(CreateMarketingTargetCommand request, CancellationToken ct = default);
    Task<Result<MarketingTargetDto>> UpdateAsync(UpdateMarketingTargetCommand request, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result<MarketingTargetDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<MarketingTargetSummaryDto>> ListAsync(ListMarketingTargetsQuery request, CancellationToken ct = default);
    Task<PagedResult<TargetMatchDto>> ListMatchesAsync(ListTargetMatchesQuery request, CancellationToken ct = default);
    Task<Result<TargetMatchDto>> UpsertMatchAsync(UpsertTargetMatchCommand request, CancellationToken ct = default);
}
