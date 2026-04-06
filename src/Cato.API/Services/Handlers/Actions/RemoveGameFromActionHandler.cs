using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class RemoveGameFromActionHandler : IRequestHandler<RemoveGameFromActionCommand, Result<bool>>
{
    private readonly IMarketingActionService _service;
    public RemoveGameFromActionHandler(IMarketingActionService service) => _service = service;
    public Task<Result<bool>> Handle(RemoveGameFromActionCommand request, CancellationToken ct)
        => _service.RemoveGameAsync(request.ActionId, request.GameId, ct);
}
