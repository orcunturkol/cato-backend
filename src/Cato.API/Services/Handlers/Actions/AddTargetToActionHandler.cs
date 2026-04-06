using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class AddTargetToActionHandler : IRequestHandler<AddTargetToActionCommand, Result<ActionTargetDto>>
{
    private readonly IMarketingActionService _service;
    public AddTargetToActionHandler(IMarketingActionService service) => _service = service;
    public Task<Result<ActionTargetDto>> Handle(AddTargetToActionCommand request, CancellationToken ct)
        => _service.AddTargetAsync(request, ct);
}
