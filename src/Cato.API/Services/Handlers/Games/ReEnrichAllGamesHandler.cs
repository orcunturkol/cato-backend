using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

public class ReEnrichAllGamesHandler : IRequestHandler<ReEnrichAllGamesCommand, Result<ReEnrichAllGamesResult>>
{
    private readonly CatoDbContext _db;
    private readonly IGameService _gameService;

    public ReEnrichAllGamesHandler(CatoDbContext db, IGameService gameService)
    {
        _db = db;
        _gameService = gameService;
    }

    public async Task<Result<ReEnrichAllGamesResult>> Handle(ReEnrichAllGamesCommand request, CancellationToken ct)
    {
        var unenrichedGames = await _db.Games
            .Where(g => g.HeaderImageUrl == null)
            .Select(g => g.Id)
            .ToListAsync(ct);

        var enriched = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var gameId in unenrichedGames)
        {
            try
            {
                var result = await _gameService.EnrichGameFromSteamAsync(gameId, ct);
                if (result.IsSuccess)
                {
                    enriched++;
                }
                else
                {
                    failed++;
                    errors.Add($"Game {gameId}: {result.ErrorMessage}");
                }
            }
            catch (HttpRequestException ex)
            {
                failed++;
                errors.Add($"Game {gameId}: {ex.Message}");
            }
        }

        return Result<ReEnrichAllGamesResult>.Success(
            new ReEnrichAllGamesResult(unenrichedGames.Count, enriched, failed, errors));
    }
}
