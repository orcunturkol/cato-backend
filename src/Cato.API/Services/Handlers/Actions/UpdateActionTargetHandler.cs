using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class UpdateActionTargetHandler : IRequestHandler<UpdateActionTargetCommand, Result<ActionTargetDto>>
{
    private readonly IMarketingActionService _service;
    public UpdateActionTargetHandler(IMarketingActionService service) => _service = service;
    public Task<Result<ActionTargetDto>> Handle(UpdateActionTargetCommand request, CancellationToken ct)
        => _service.UpdateTargetAsync(request, ct);
}
