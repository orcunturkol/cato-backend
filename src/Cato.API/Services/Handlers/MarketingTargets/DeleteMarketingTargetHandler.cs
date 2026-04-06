using Cato.API.DTOs;
using Cato.API.Models.MarketingTargets;
using MediatR;

namespace Cato.API.Services.Handlers.MarketingTargets;

public class DeleteMarketingTargetHandler : IRequestHandler<DeleteMarketingTargetCommand, Result<bool>>
{
    private readonly IMarketingTargetService _service;
    public DeleteMarketingTargetHandler(IMarketingTargetService service) => _service = service;
    public Task<Result<bool>> Handle(DeleteMarketingTargetCommand request, CancellationToken ct)
        => _service.DeleteAsync(request.Id, ct);
}
