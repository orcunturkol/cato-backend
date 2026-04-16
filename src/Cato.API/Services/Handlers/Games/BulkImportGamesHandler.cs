using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using Cato.Infrastructure.Steam;
using ClosedXML.Excel;
using MediatR;

namespace Cato.API.Services.Handlers.Games;

public class BulkImportGamesHandler : IRequestHandler<BulkImportGamesCommand, Result<BulkImportResult>>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BulkImportGamesHandler> _logger;

    public BulkImportGamesHandler(IServiceScopeFactory scopeFactory, ILogger<BulkImportGamesHandler> logger)
    {
        _scopeFactory = scopeFactory;
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

        if (appIds.Count == 0)
            return Result<BulkImportResult>.Success(new BulkImportResult(0));

        _logger.LogInformation("Bulk import: queuing {Count} games for background processing", appIds.Count);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var gameService = scope.ServiceProvider.GetRequiredService<IGameService>();
            var created = 0;
            var enriched = 0;
            var failed = 0;

            foreach (var appId in appIds)
            {
                var createResult = await gameService.CreateGameAsync(
                    new CreateGameCommand(appId, null, "Sourcing", null, null), CancellationToken.None);

                if (!createResult.IsSuccess)
                {
                    _logger.LogWarning("Bulk import: skipped AppId {AppId} — {Error}", appId, createResult.ErrorMessage);
                    failed++;
                    continue;
                }

                created++;

                var enrichResult = await gameService.EnrichGameFromSteamAsync(createResult.Data!.Id, CancellationToken.None);
                if (enrichResult.IsSuccess)
                    enriched++;
                else
                    _logger.LogWarning("Bulk import: enrich failed for AppId {AppId} — {Error}", appId, enrichResult.ErrorMessage);
            }

            _logger.LogInformation(
                "Bulk import: completed — {Created} created, {Enriched} enriched, {Failed} failed out of {Total}",
                created, enriched, failed, appIds.Count);
        });

        return Result<BulkImportResult>.Success(new BulkImportResult(appIds.Count));
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
