using System.Text.Json;
using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestPeakCcuHandler : IRequestHandler<IngestPeakCcuCommand, IngestionResult>
{
    private readonly CatoDbContext _db;
    private readonly ILogger<IngestPeakCcuHandler> _logger;

    public IngestPeakCcuHandler(CatoDbContext db, ILogger<IngestPeakCcuHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IngestionResult> Handle(IngestPeakCcuCommand request, CancellationToken ct)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "gamalytic_peak_ccu",
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

            // peak_ccu_history.json: { "peakCcuHistory": [{ "timestamp": "2024-01-15T00:00:00", "peakCcu": 123 }, ...] }
            JsonElement historyArray;
            if (doc.RootElement.TryGetProperty("peakCcuHistory", out historyArray))
            {
                // Array format from Gamalytic
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // Direct array format
                historyArray = doc.RootElement;
            }
            else
            {
                throw new InvalidOperationException("Unrecognized peak CCU file format");
            }

            foreach (var entry in historyArray.EnumerateArray())
            {
                processed++;
                try
                {
                    DateTime timestamp;
                    if (entry.TryGetProperty("timestamp", out var tsElement))
                    {
                        if (tsElement.ValueKind == JsonValueKind.Number)
                        {
                            // Unix timestamp in milliseconds
                            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsElement.GetInt64()).UtcDateTime;
                        }
                        else
                        {
                            // ISO string
                            timestamp = DateTime.Parse(tsElement.GetString()!, null,
                                System.Globalization.DateTimeStyles.AdjustToUniversal);
                        }
                    }
                    else
                    {
                        failed++;
                        continue;
                    }

                    var ccuCount = 0;
                    if (entry.TryGetProperty("peakCcu", out var peakCcuEl))
                        ccuCount = peakCcuEl.GetInt32();
                    else if (entry.TryGetProperty("ccu", out var ccuEl))
                        ccuCount = ccuEl.GetInt32();

                    // Check for existing (idempotency)
                    var exists = await _db.CcuHistories.AnyAsync(
                        c => c.GameId == game.Id &&
                             c.Timestamp == timestamp &&
                             c.Source == "Gamalytic",
                        ct);

                    if (!exists)
                    {
                        _db.CcuHistories.Add(new CcuHistory
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            Timestamp = timestamp,
                            CcuCount = ccuCount,
                            Source = "Gamalytic"
                        });
                        inserted++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Failed to process CCU entry");
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Peak CCU ingestion complete: {Processed} processed, {Inserted} inserted, {Failed} failed",
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
