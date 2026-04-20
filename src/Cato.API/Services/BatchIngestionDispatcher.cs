using System.Text.Json;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace Cato.API.Services;

/// <summary>
/// Dispatches a batch ingestion message: validates schema, routes by source,
/// stages all items under one transaction, commits atomically. Any exception
/// rolls back the whole batch; the consumer nacks without requeue so the
/// message lands in the DLQ.
/// </summary>
public class BatchIngestionDispatcher : IBatchIngestionDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const int SupportedSchemaVersion = 1;

    private readonly CatoDbContext _db;
    private readonly IIngestionService _ingestionService;
    private readonly ILogger<BatchIngestionDispatcher> _logger;

    public BatchIngestionDispatcher(
        CatoDbContext db,
        IIngestionService ingestionService,
        ILogger<BatchIngestionDispatcher> logger)
    {
        _db = db;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    public async Task DispatchAsync(string messageJson, CancellationToken ct)
    {
        var batch = JsonSerializer.Deserialize<BatchIngestionMessage>(messageJson, JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize batch ingestion message");

        if (batch.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidOperationException(
                $"Unsupported batch schema_version={batch.SchemaVersion} (expected {SupportedSchemaVersion})");

        if (string.IsNullOrWhiteSpace(batch.Source))
            throw new InvalidOperationException("Batch message missing 'source'");

        using (LogContext.PushProperty("BatchId", batch.BatchId))
        using (LogContext.PushProperty("Source", batch.Source))
        using (LogContext.PushProperty("ItemCount", batch.Items.Count))
        {
            _logger.LogInformation("Batch dispatch start");

            var log = new IngestionLog
            {
                Id = Guid.NewGuid(),
                Source = $"batch:{batch.Source}",
                StartTime = DateTime.UtcNow,
                Status = "Running",
                FilePath = batch.BatchId.ToString()
            };
            _db.IngestionLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                int processed = 0, inserted = 0, updated = 0, failed = 0;

                foreach (var item in batch.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    var r = await DispatchItemAsync(batch.Source, item, ct);
                    processed += r.Processed;
                    inserted  += r.Inserted;
                    updated   += r.Updated;
                    failed    += r.Failed;
                }

                await _db.SaveChangesAsync(ct);

                log.EndTime = DateTime.UtcNow;
                log.Status = "Completed";
                log.RecordsProcessed = processed;
                log.RecordsInserted = inserted;
                log.RecordsUpdated = updated;
                log.RecordsFailed = failed;
                await _db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Batch dispatch complete processed={Processed} inserted={Inserted} updated={Updated} failed={Failed}",
                    processed, inserted, updated, failed);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                log.EndTime = DateTime.UtcNow;
                log.Status = "Failed";
                log.ErrorMessage = ex.Message;
                try { await _db.SaveChangesAsync(ct); }
                catch (Exception logEx) { _logger.LogWarning(logEx, "Failed to persist failed batch log entry"); }
                throw;
            }
        }
    }

    private Task<ItemIngestResult> DispatchItemAsync(string source, BatchIngestionItem item, CancellationToken ct) =>
        source switch
        {
            "steam_current_players"     => _ingestionService.IngestCcuItemAsync(item.AppId, item.ScrapedAt, item.Data, ct),
            "group_member_count"        => _ingestionService.IngestGroupMemberCountItemAsync(item.AppId, item.ScrapedAt, item.Data, ct),
            "steamdb_most_wished"       => _ingestionService.IngestSteamDbSnapshotItemAsync(item.AppId, item.ScrapedAt, item.Data, ct),
            "steamdb_wishlist_activity" => _ingestionService.IngestSteamDbSnapshotItemAsync(item.AppId, item.ScrapedAt, item.Data, ct),
            "steam_financial"           => _ingestionService.IngestFinancialDataItemAsync(item.AppId, item.ScrapedAt, item.Data, ct),
            _ => throw new InvalidOperationException($"Unknown batch source: '{source}'"),
        };
}
