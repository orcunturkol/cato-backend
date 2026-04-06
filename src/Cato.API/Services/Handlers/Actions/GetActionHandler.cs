using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class GetActionHandler : IRequestHandler<GetActionQuery, Result<MarketingActionDto>>
{
    private readonly IMarketingActionService _service;
    public GetActionHandler(IMarketingActionService service) => _service = service;
    public Task<Result<MarketingActionDto>> Handle(GetActionQuery request, CancellationToken ct)
        => _service.GetByIdAsync(request.Id, ct);
}
