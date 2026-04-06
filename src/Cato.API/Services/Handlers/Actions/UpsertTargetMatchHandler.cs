using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class UpsertTargetMatchHandler : IRequestHandler<UpsertTargetMatchCommand, Result<TargetMatchDto>>
{
    private readonly IMarketingTargetService _service;
    public UpsertTargetMatchHandler(IMarketingTargetService service) => _service = service;
    public Task<Result<TargetMatchDto>> Handle(UpsertTargetMatchCommand request, CancellationToken ct)
        => _service.UpsertMatchAsync(request, ct);
}
