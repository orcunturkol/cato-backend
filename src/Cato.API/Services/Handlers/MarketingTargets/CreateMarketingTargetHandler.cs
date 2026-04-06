using Cato.API.DTOs;
using Cato.API.Models.MarketingTargets;
using MediatR;

namespace Cato.API.Services.Handlers.MarketingTargets;

public class CreateMarketingTargetHandler : IRequestHandler<CreateMarketingTargetCommand, Result<MarketingTargetDto>>
{
    private readonly IMarketingTargetService _service;
    public CreateMarketingTargetHandler(IMarketingTargetService service) => _service = service;
    public Task<Result<MarketingTargetDto>> Handle(CreateMarketingTargetCommand request, CancellationToken ct)
        => _service.CreateAsync(request, ct);
}
