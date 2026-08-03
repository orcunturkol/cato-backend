using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

public class EnrichGameByAppIdHandler : IRequestHandler<EnrichGameByAppIdCommand, Result<GameDto>>
{
    private readonly CatoDbContext _db;
    private readonly IMediator _mediator;

    public EnrichGameByAppIdHandler(CatoDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async Task<Result<GameDto>> Handle(EnrichGameByAppIdCommand request, CancellationToken ct)
    {
        var id = await _db.Games.AsNoTracking()
            .Where(g => g.AppId == request.AppId)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);

        return id is null
            ? Result<GameDto>.Failure($"Game with AppId {request.AppId} not found.")
            : await _mediator.Send(new EnrichGameFromSteamCommand(id.Value), ct);
    }
}
