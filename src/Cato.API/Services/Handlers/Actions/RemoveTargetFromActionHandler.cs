using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class RemoveTargetFromActionHandler : IRequestHandler<RemoveTargetFromActionCommand, Result<bool>>
{
    private readonly IMarketingActionService _service;
    public RemoveTargetFromActionHandler(IMarketingActionService service) => _service = service;
    public Task<Result<bool>> Handle(RemoveTargetFromActionCommand request, CancellationToken ct)
        => _service.RemoveTargetAsync(request.ActionId, request.TargetId, ct);
}
