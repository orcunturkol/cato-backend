using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

public class GetGameDetailsHandler : IRequestHandler<GetGameDetailsQuery, Result<GameDto>>
{
    private readonly CatoDbContext _db;

    public GetGameDetailsHandler(CatoDbContext db) => _db = db;

    public async Task<Result<GameDto>> Handle(GetGameDetailsQuery request, CancellationToken ct)
    {
        var game = await _db.Games
            .AsNoTracking()
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstOrDefaultAsync(g => g.Id == request.Id, ct);

        return game is null
            ? Result<GameDto>.Failure($"Game with Id {request.Id} not found.")
            : Result<GameDto>.Success(game.ToDto());
    }
}
