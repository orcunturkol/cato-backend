using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class ListActionsHandler : IRequestHandler<ListActionsQuery, PagedResult<MarketingActionSummaryDto>>
{
    private readonly IMarketingActionService _service;
    public ListActionsHandler(IMarketingActionService service) => _service = service;
    public Task<PagedResult<MarketingActionSummaryDto>> Handle(ListActionsQuery request, CancellationToken ct)
        => _service.ListAsync(request, ct);
}
