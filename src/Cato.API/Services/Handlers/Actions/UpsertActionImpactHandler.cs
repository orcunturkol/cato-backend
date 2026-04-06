using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class UpsertActionImpactHandler : IRequestHandler<UpsertActionImpactCommand, Result<ActionImpactDto>>
{
    private readonly IMarketingActionService _service;
    public UpsertActionImpactHandler(IMarketingActionService service) => _service = service;
    public Task<Result<ActionImpactDto>> Handle(UpsertActionImpactCommand request, CancellationToken ct)
        => _service.UpsertImpactAsync(request, ct);
}
