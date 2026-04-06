using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class ListTargetMatchesHandler : IRequestHandler<ListTargetMatchesQuery, PagedResult<TargetMatchDto>>
{
    private readonly IMarketingTargetService _service;
    public ListTargetMatchesHandler(IMarketingTargetService service) => _service = service;
    public Task<PagedResult<TargetMatchDto>> Handle(ListTargetMatchesQuery request, CancellationToken ct)
        => _service.ListMatchesAsync(request, ct);
}
