using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class CreateActionHandler : IRequestHandler<CreateActionCommand, Result<MarketingActionDto>>
{
    private readonly IMarketingActionService _service;
    public CreateActionHandler(IMarketingActionService service) => _service = service;
    public Task<Result<MarketingActionDto>> Handle(CreateActionCommand request, CancellationToken ct)
        => _service.CreateAsync(request, ct);
}
