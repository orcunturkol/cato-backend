using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class BulkImportGamesHandler : IRequestHandler<BulkImportGamesCommand, Result<BulkImportResult>>
{
    private readonly IGameService _gameService;
    private readonly ILogger<BulkImportGamesHandler> _logger;

    public BulkImportGamesHandler(IGameService gameService, ILogger<BulkImportGamesHandler> logger)
    {
        _gameService = gameService;
        _logger = logger;
    }

    public async Task<Result<BulkImportResult>> Handle(BulkImportGamesCommand request, CancellationToken ct)
    {
        if (!File.Exists(request.CsvFilePath))
            return Result<BulkImportResult>.Failure($"File not found: {request.CsvFilePath}");

        var lines = await File.ReadAllLinesAsync(request.CsvFilePath, ct);
        var appIds = new List<int>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // CSV format: "AppID,<number>"
            var parts = line.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var appId))
                appIds.Add(appId);
            else if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out var singleAppId))
                appIds.Add(singleAppId);
        }

        var created = 0;
        var enriched = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var appId in appIds)
        {
            // Step 1: Create the game
            var createResult = await _gameService.CreateGameAsync(
                new CreateGameCommand(appId, null, "Sourcing", null, null), ct);

            if (!createResult.IsSuccess)
            {
                _logger.LogWarning("Skipped AppId {AppId}: {Error}", appId, createResult.ErrorMessage);
                skipped++;
                continue;
            }

            created++;
            var gameId = createResult.Data!.Id;

            // Step 2: Enrich from Steam
            try
            {
                var enrichResult = await _gameService.EnrichGameFromSteamAsync(gameId, ct);
                if (enrichResult.IsSuccess)
                {
                    enriched++;
                    _logger.LogInformation("Enriched AppId {AppId} ({Name})", appId, enrichResult.Data!.Name);
                }
                else
                {
                    errors.Add($"AppId {appId}: enrich failed - {enrichResult.ErrorMessage}");
                    _logger.LogWarning("Enrich failed for AppId {AppId}: {Error}", appId, enrichResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"AppId {appId}: enrich exception - {ex.Message}");
                _logger.LogError(ex, "Enrich exception for AppId {AppId}", appId);
            }
        }

        var result = new BulkImportResult(appIds.Count, created, enriched, skipped, errors);
        return Result<BulkImportResult>.Success(result);
    }
}
