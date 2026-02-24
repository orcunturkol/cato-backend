using System.Text.Json;
using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestFinancialDataHandler : IRequestHandler<IngestFinancialDataCommand, IngestionResult>
{
    private readonly CatoDbContext _db;
    private readonly ILogger<IngestFinancialDataHandler> _logger;

    public IngestFinancialDataHandler(CatoDbContext db, ILogger<IngestFinancialDataHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IngestionResult> Handle(IngestFinancialDataCommand request, CancellationToken ct)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "steam_financial",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FilePath
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            if (!File.Exists(request.FilePath))
                throw new FileNotFoundException($"File not found: {request.FilePath}");

            var json = await File.ReadAllTextAsync(request.FilePath, ct);
            var doc = JsonDocument.Parse(json);

            int processed = 0, inserted = 0, failed = 0;

            // Financial data structure: { "regionalHistory": { "US": [...], "DE": [...] } }
            if (doc.RootElement.TryGetProperty("regionalHistory", out var regionalHistory))
            {
                foreach (var country in regionalHistory.EnumerateObject())
                {
                    var countryCode = country.Name;
                    foreach (var entry in country.Value.EnumerateArray())
                    {
                        processed++;
                        try
                        {
                            var timestamp = entry.GetProperty("timestamp").GetInt64();
                            var saleDate = DateOnly.FromDateTime(
                                DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime);
                            var copiesSold = entry.GetProperty("copiesSold").GetInt32();
                            var revenue = entry.GetProperty("revenue").GetDecimal();

                            // Check for existing record (idempotency)
                            var exists = await _db.SteamSaleFinancials.AnyAsync(
                                s => s.GameId == game.Id &&
                                     s.SaleDate == saleDate &&
                                     s.CountryCode == countryCode,
                                ct);

                            if (!exists)
                            {
                                _db.SteamSaleFinancials.Add(new SteamSaleFinancial
                                {
                                    Id = Guid.NewGuid(),
                                    GameId = game.Id,
                                    SaleDate = saleDate,
                                    CountryCode = countryCode,
                                    SalesUnits = copiesSold,
                                    GrossRevenueUsd = revenue,
                                    NetRevenueUsd = revenue
                                });
                                inserted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            _logger.LogWarning(ex, "Failed to process financial entry for {Country}", countryCode);
                        }
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Financial ingestion complete: {Processed} processed, {Inserted} inserted, {Failed} failed",
                processed, inserted, failed);

            return new IngestionResult(processed, inserted, 0, failed, log.Id);
        }
        catch (Exception ex)
        {
            log.EndTime = DateTime.UtcNow;
            log.Status = "Failed";
            log.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }
}
