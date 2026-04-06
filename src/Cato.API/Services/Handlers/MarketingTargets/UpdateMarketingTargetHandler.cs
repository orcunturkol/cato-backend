using Cato.API.DTOs;
using Cato.API.Models.MarketingTargets;
using MediatR;

namespace Cato.API.Services.Handlers.MarketingTargets;

public class UpdateMarketingTargetHandler : IRequestHandler<UpdateMarketingTargetCommand, Result<MarketingTargetDto>>
{
    private readonly IMarketingTargetService _service;
    public UpdateMarketingTargetHandler(IMarketingTargetService service) => _service = service;
    public Task<Result<MarketingTargetDto>> Handle(UpdateMarketingTargetCommand request, CancellationToken ct)
        => _service.UpdateAsync(request, ct);
}
