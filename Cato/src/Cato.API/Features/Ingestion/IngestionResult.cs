namespace Cato.API.Features.Ingestion;

/// <summary>
/// Result returned by all ingestion handlers.
/// </summary>
public record IngestionResult(
    int RecordsProcessed,
    int RecordsInserted,
    int RecordsUpdated,
    int RecordsFailed,
    Guid IngestionLogId);
