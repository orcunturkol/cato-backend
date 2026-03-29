using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
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

public class ReEnrichAllGamesBackgroundHandler : IRequestHandler<ReEnrichAllGamesBackgroundCommand, Result<ReEnrichAllGamesBackgroundResult>>
{
    private readonly CatoDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReEnrichAllGamesBackgroundHandler> _logger;

    public ReEnrichAllGamesBackgroundHandler(
        CatoDbContext db,
        IServiceScopeFactory scopeFactory,
        ILogger<ReEnrichAllGamesBackgroundHandler> logger)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<Result<ReEnrichAllGamesBackgroundResult>> Handle(ReEnrichAllGamesBackgroundCommand request, CancellationToken ct)
    {
        var unenrichedGameIds = await _db.Games
            .Where(g => g.HeaderImageUrl == null)
            .Select(g => g.Id)
            .ToListAsync(ct);

        if (unenrichedGameIds.Count == 0)
            return Result<ReEnrichAllGamesBackgroundResult>.Success(new ReEnrichAllGamesBackgroundResult(0));

        _logger.LogInformation("Re-enrich: queuing {Count} unenriched games for background enrichment", unenrichedGameIds.Count);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var enrichment = scope.ServiceProvider.GetRequiredService<ISteamGameEnrichmentService>();
            var enriched = 0;
            var failed = 0;

            foreach (var gameId in unenrichedGameIds)
            {
                try
                {
                    var success = await enrichment.EnrichGameAsync(gameId);
                    if (success) enriched++;
                    else failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Re-enrich background: failed to enrich game {GameId}", gameId);
                }
            }

            _logger.LogInformation("Re-enrich background: completed — {Enriched} enriched, {Failed} failed out of {Total}",
                enriched, failed, unenrichedGameIds.Count);
        });

        return Result<ReEnrichAllGamesBackgroundResult>.Success(
            new ReEnrichAllGamesBackgroundResult(unenrichedGameIds.Count));
    }
}
