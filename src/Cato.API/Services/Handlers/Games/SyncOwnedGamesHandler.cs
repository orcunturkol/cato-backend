using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

/// <summary>
/// Makes every app the Steamworks login can see an <c>Owned</c> game before the
/// Steamworks wishlist collector publishes its data. The row work is synchronous
/// on purpose: the collector waits for it, so the wishlist batch always finds the
/// game instead of stubbing it as <c>Other</c> (and a concurrent create could trip
/// the unique AppId index and send the whole batch to the DLQ). Only the slow part,
/// Steam store enrichment, runs in the background.
/// </summary>
public class SyncOwnedGamesHandler : IRequestHandler<SyncOwnedGamesCommand, Result<SyncOwnedGamesResult>>
{
    private const string OwnedType = "Owned";

    private readonly CatoDbContext _db;
    private readonly IGameService _gameService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncOwnedGamesHandler> _logger;

    public SyncOwnedGamesHandler(
        CatoDbContext db,
        IGameService gameService,
        IServiceScopeFactory scopeFactory,
        ILogger<SyncOwnedGamesHandler> logger)
    {
        _db = db;
        _gameService = gameService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<Result<SyncOwnedGamesResult>> Handle(SyncOwnedGamesCommand request, CancellationToken ct)
    {
        var entries = request.Games.GroupBy(g => g.AppId).Select(g => g.Last()).ToList();
        var appIds = entries.Select(e => e.AppId).ToList();

        var known = await _db.Games
            .AsNoTracking()
            .Where(g => appIds.Contains(g.AppId))
            .Select(g => new { g.Id, g.AppId, g.GameType, g.LastEnrichedAt })
            .ToDictionaryAsync(g => g.AppId, ct);

        int created = 0, promoted = 0, unchanged = 0, failed = 0;
        var toEnrich = new List<(int AppId, Guid GameId)>();

        foreach (var entry in entries)
        {
            known.TryGetValue(entry.AppId, out var current);

            if (current is not null && current.GameType == OwnedType)
            {
                unchanged++;
                if (current.LastEnrichedAt is null)
                    toEnrich.Add((entry.AppId, current.Id));
                continue;
            }

            // Creates the row, or changes an existing row's GameType. Both paths
            // write Redis after SaveChanges, so the game joins the tracked sets.
            var result = await _gameService.UpsertGameAsync(
                new CreateGameCommand(entry.AppId, entry.Name, OwnedType, null, null), ct);

            if (!result.IsSuccess)
            {
                failed++;
                _logger.LogWarning("Owned sync: skipped AppId {AppId} — {Error}", entry.AppId, result.ErrorMessage);
                continue;
            }

            if (result.Data!.WasCreated)
            {
                created++;
                toEnrich.Add((entry.AppId, result.Data.Game.Id));
                continue;
            }

            promoted++;
            _logger.LogInformation("Owned sync: promoted AppId {AppId} from {OldType} to Owned",
                entry.AppId, current!.GameType);
            if (current.LastEnrichedAt is null)
                toEnrich.Add((entry.AppId, current.Id));
        }

        if (toEnrich.Count > 0)
            QueueEnrichment(toEnrich);

        _logger.LogInformation(
            "Owned sync: {Created} created, {Promoted} promoted, {Unchanged} unchanged, {Failed} failed, {Enrich} queued for enrichment",
            created, promoted, unchanged, failed, toEnrich.Count);

        return Result<SyncOwnedGamesResult>.Success(
            new SyncOwnedGamesResult(created, promoted, unchanged, failed, toEnrich.Count));
    }

    private void QueueEnrichment(List<(int AppId, Guid GameId)> games)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();

            foreach (var (appId, gameId) in games)
            {
                try
                {
                    var result = await gameService.EnrichGameFromSteamAsync(gameId, CancellationToken.None);
                    if (!result.IsSuccess)
                        _logger.LogWarning("Owned sync: enrich failed for AppId {AppId} — {Error}", appId, result.ErrorMessage);
                }
                catch (Exception ex)
                {
                    // Nothing awaits this task; an escaped exception would vanish.
                    _logger.LogWarning(ex, "Owned sync: enrich threw for AppId {AppId}", appId);
                }
            }
        });
    }
}
