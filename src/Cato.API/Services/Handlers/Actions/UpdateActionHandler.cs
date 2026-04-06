using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class UpdateActionHandler : IRequestHandler<UpdateActionCommand, Result<MarketingActionDto>>
{
    private readonly IMarketingActionService _service;
    public UpdateActionHandler(IMarketingActionService service) => _service = service;
    public Task<Result<MarketingActionDto>> Handle(UpdateActionCommand request, CancellationToken ct)
        => _service.UpdateAsync(request, ct);
}
