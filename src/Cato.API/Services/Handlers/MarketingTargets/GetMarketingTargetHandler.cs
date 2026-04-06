using Cato.API.DTOs;
using Cato.API.Models.MarketingTargets;
using MediatR;

namespace Cato.API.Services.Handlers.MarketingTargets;

public class GetMarketingTargetHandler : IRequestHandler<GetMarketingTargetQuery, Result<MarketingTargetDto>>
{
    private readonly IMarketingTargetService _service;
    public GetMarketingTargetHandler(IMarketingTargetService service) => _service = service;
    public Task<Result<MarketingTargetDto>> Handle(GetMarketingTargetQuery request, CancellationToken ct)
        => _service.GetByIdAsync(request.Id, ct);
}
