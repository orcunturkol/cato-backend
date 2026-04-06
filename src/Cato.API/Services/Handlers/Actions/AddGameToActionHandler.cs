using Cato.API.DTOs;
using Cato.API.Models.Actions;
using MediatR;

namespace Cato.API.Services.Handlers.Actions;

public class AddGameToActionHandler : IRequestHandler<AddGameToActionCommand, Result<GameActionDto>>
{
    private readonly IMarketingActionService _service;
    public AddGameToActionHandler(IMarketingActionService service) => _service = service;
    public Task<Result<GameActionDto>> Handle(AddGameToActionCommand request, CancellationToken ct)
        => _service.AddGameAsync(request, ct);
}
