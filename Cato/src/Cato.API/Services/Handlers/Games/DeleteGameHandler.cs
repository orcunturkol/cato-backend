using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

public class DeleteGameHandler : IRequestHandler<DeleteGameCommand, Result<bool>>
{
    private readonly CatoDbContext _db;

    public DeleteGameHandler(CatoDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(DeleteGameCommand request, CancellationToken ct)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == request.Id, ct);
        if (game is null)
            return Result<bool>.Failure($"Game with Id {request.Id} not found.");

        _db.Games.Remove(game); // Cascade deletes genres & tags
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
