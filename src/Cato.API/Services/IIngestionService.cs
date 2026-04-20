using System.Text.Json;
using Cato.API.DTOs;
using Cato.API.Models.Ingestion;

namespace Cato.API.Services;

public interface IIngestionService
{
    Task<IngestionResult> IngestPeakCcuAsync(IngestPeakCcuCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestCcuAsync(IngestCcuCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestFinancialDataAsync(IngestFinancialDataCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestWishlistDataAsync(IngestWishlistDataCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestOwnedGameDataAsync(IngestOwnedGameDataCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestGroupMemberCountAsync(IngestGroupMemberCountCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestSteamDbSnapshotAsync(IngestSteamDbSnapshotCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestRegionalPricesAsync(IngestRegionalPricesCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestWishlistInsightsAsync(IngestWishlistInsightsCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestStoreTrafficAsync(IngestStoreTrafficCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestNewsAsync(IngestNewsCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestPatchNotesAsync(IngestPatchNotesCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestActiveUsersHistoryAsync(IngestActiveUsersHistoryCommand command, CancellationToken ct = default);
    Task<IngestionResult> IngestDemoDownloadsAsync(IngestDemoDownloadsCommand command, CancellationToken ct = default);
    Task<List<IngestionLogDto>> GetIngestionLogsAsync(GetIngestionLogsQuery query, CancellationToken ct = default);

    // ── Batch-item mode (used by BatchIngestionDispatcher) ─────────────────
    // These operate on an already-parsed JsonElement payload, stage entities on
    // the shared DbContext, and return counts without calling SaveChanges.
    // The caller is expected to wrap the whole batch in a single transaction.
    Task<ItemIngestResult> IngestCcuItemAsync(int appId, DateTimeOffset scrapedAt, JsonElement data, CancellationToken ct = default);
    Task<ItemIngestResult> IngestGroupMemberCountItemAsync(int appId, DateTimeOffset scrapedAt, JsonElement data, CancellationToken ct = default);
    Task<ItemIngestResult> IngestSteamDbSnapshotItemAsync(int appId, DateTimeOffset scrapedAt, JsonElement data, CancellationToken ct = default);
    Task<ItemIngestResult> IngestFinancialDataItemAsync(int appId, DateTimeOffset scrapedAt, JsonElement data, CancellationToken ct = default);
}

public readonly record struct ItemIngestResult(int Processed, int Inserted, int Updated, int Failed);
