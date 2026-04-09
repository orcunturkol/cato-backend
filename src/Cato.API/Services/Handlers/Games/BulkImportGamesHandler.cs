using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using ClosedXML.Excel;
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
        var ext = Path.GetExtension(request.FileName).ToLowerInvariant();
        List<int> appIds;
        try
        {
            appIds = ext switch
            {
                ".xlsx" => ParseXlsx(request.Content),
                ".csv" or ".txt" or "" => await ParseCsvAsync(request.Content, ct),
                _ => throw new InvalidOperationException($"Unsupported file type: {ext}")
            };
        }
        catch (InvalidOperationException ex)
        {
            return Result<BulkImportResult>.Failure(ex.Message);
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

    private static async Task<List<int>> ParseCsvAsync(Stream content, CancellationToken ct)
    {
        var appIds = new List<int>();
        using var reader = new StreamReader(content);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // CSV format: "<number>" or "AppID,<number>"
            var parts = line.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var appId))
                appIds.Add(appId);
            else if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out var singleAppId))
                appIds.Add(singleAppId);
        }
        return appIds;
    }

    private static List<int> ParseXlsx(Stream content)
    {
        var appIds = new List<int>();
        using var workbook = new XLWorkbook(content);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("XLSX file contains no worksheets.");

        foreach (var row in sheet.RowsUsed())
        {
            foreach (var cell in row.CellsUsed())
            {
                if (cell.TryGetValue<int>(out var appId))
                {
                    appIds.Add(appId);
                    break; // first numeric cell per row
                }
                var text = cell.GetString().Trim();
                if (int.TryParse(text, out var parsed))
                {
                    appIds.Add(parsed);
                    break;
                }
            }
        }
        return appIds;
    }
}
