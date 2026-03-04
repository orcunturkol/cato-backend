using Cato.API.DTOs;
using Cato.API.Models.Ingestion;

namespace Cato.API.Services;

public interface IIngestionService
{
    Task<IngestionResult> IngestPeakCcuAsync(IngestPeakCcuCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestFinancialDataAsync(IngestFinancialDataCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestWishlistDataAsync(IngestWishlistDataCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestOwnedGameDataAsync(IngestOwnedGameDataCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestGroupMemberCountAsync(IngestGroupMemberCountCommand command, CancellationToken ct = default);
    Task<List<IngestionLogDto>> GetIngestionLogsAsync(GetIngestionLogsQuery query, CancellationToken ct = default);
}
