using MediatR;

namespace Cato.API.Models.Ingestion;

public record GetIngestionLogsQuery(string? Source, int Limit = 20) : IRequest<List<IngestionLogDto>>;

public record IngestionLogDto(
    Guid Id,
    string Source,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    int RecordsProcessed,
    int RecordsInserted,
    int RecordsUpdated,
    int RecordsFailed,
    string? ErrorMessage,
    string? FilePath);
