using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class DeleteActionHandler : IRequestHandler<DeleteActionCommand, Result<bool>>
{
    private readonly IMarketingActionService _service;
    public DeleteActionHandler(IMarketingActionService service) => _service = service;
    public Task<Result<bool>> Handle(DeleteActionCommand request, CancellationToken ct)
        => _service.DeleteAsync(request.Id, ct);
}
