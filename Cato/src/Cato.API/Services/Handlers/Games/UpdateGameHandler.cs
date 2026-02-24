using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

public class UpdateGameHandler : IRequestHandler<UpdateGameCommand, Result<GameDto>>
{
    private readonly CatoDbContext _db;

    public UpdateGameHandler(CatoDbContext db) => _db = db;

    public async Task<Result<GameDto>> Handle(UpdateGameCommand request, CancellationToken ct)
    {
        var game = await _db.Games
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .Include(g => g.Genres)
            .Include(g => g.Tags)
            .FirstOrDefaultAsync(g => g.Id == request.Id, ct);

        if (game is null)
            return Result<GameDto>.Failure($"Game with Id {request.Id} not found.");

        // Partial update — only set fields that were provided
        if (request.Name is not null) game.Name = request.Name;
        if (request.GameType is not null) game.GameType = request.GameType;
        if (request.PriceUsd.HasValue) game.PriceUsd = request.PriceUsd.Value;
        if (request.IsEarlyAccess.HasValue) game.IsEarlyAccess = request.IsEarlyAccess.Value;
        if (request.IsReleased.HasValue) game.IsReleased = request.IsReleased.Value;
        if (request.ShortDescription is not null) game.ShortDescription = request.ShortDescription;
        if (request.Website is not null) game.Website = request.Website;
        if (request.SteamReviewScore is not null) game.SteamReviewScore = request.SteamReviewScore;
        if (request.ReviewCount.HasValue) game.ReviewCount = request.ReviewCount.Value;
        if (request.FollowersCount.HasValue) game.FollowersCount = request.FollowersCount.Value;

        await _db.SaveChangesAsync(ct);

        return Result<GameDto>.Success(game.ToDto());
    }
}
