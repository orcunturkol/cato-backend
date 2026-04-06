using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class GetActionImpactHandler : IRequestHandler<GetActionImpactQuery, Result<ActionImpactDto>>
{
    private readonly IMarketingActionService _service;
    public GetActionImpactHandler(IMarketingActionService service) => _service = service;
    public Task<Result<ActionImpactDto>> Handle(GetActionImpactQuery request, CancellationToken ct)
        => _service.GetImpactAsync(request.ActionId, ct);
}
