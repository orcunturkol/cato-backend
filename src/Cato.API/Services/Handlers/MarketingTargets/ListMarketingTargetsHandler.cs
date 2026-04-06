using Cato.API.DTOs;
using Cato.API.Models.MarketingTargets;
using MediatR;

namespace Cato.API.Services.Handlers.MarketingTargets;

public class ListMarketingTargetsHandler : IRequestHandler<ListMarketingTargetsQuery, PagedResult<MarketingTargetSummaryDto>>
{
    private readonly IMarketingTargetService _service;
    public ListMarketingTargetsHandler(IMarketingTargetService service) => _service = service;
    public Task<PagedResult<MarketingTargetSummaryDto>> Handle(ListMarketingTargetsQuery request, CancellationToken ct)
        => _service.ListAsync(request, ct);
}
