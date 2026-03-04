using System.Globalization;
using System.Text.Json;
using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services;

public class IngestionService : IIngestionService
{
    private readonly CatoDbContext _db;
    private readonly ILogger<IngestionService> _logger;
    private readonly IGameService _gameService;

    public IngestionService(CatoDbContext db, ILogger<IngestionService> logger, IGameService gameService)
    {
        _db = db;
        _logger = logger;
        _gameService = gameService;
    }

    public async Task<IngestionResult> IngestPeakCcuAsync(IngestPeakCcuCommand request, CancellationToken ct = default)
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

            JsonElement historyArray;
            if (doc.RootElement.TryGetProperty("peakCcuHistory", out historyArray))
            {
                // Array format from Gamalytic
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
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
                        timestamp = tsElement.ValueKind == JsonValueKind.Number
                            ? DateTimeOffset.FromUnixTimeMilliseconds(tsElement.GetInt64()).UtcDateTime
                            : DateTime.Parse(tsElement.GetString()!, null,
                                System.Globalization.DateTimeStyles.AdjustToUniversal);
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

                    var exists = await _db.CcuHistories.AnyAsync(
                        c => c.GameId == game.Id && c.Timestamp == timestamp && c.Source == "Gamalytic", ct);

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

    public async Task<IngestionResult> IngestFinancialDataAsync(IngestFinancialDataCommand request, CancellationToken ct = default)
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

                            var exists = await _db.SteamSaleFinancials.AnyAsync(
                                s => s.GameId == game.Id && s.SaleDate == saleDate && s.CountryCode == countryCode, ct);

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

    public async Task<IngestionResult> IngestWishlistDataAsync(IngestWishlistDataCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "steamworks_wishlist",
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

            var lines = await File.ReadAllLinesAsync(request.FilePath, ct);

            int processed = 0, inserted = 0, failed = 0;

            // CSV: first header row starts with "DateLocal,"
            var dataStart = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("DateLocal,", StringComparison.OrdinalIgnoreCase))
                {
                    dataStart = i + 1;
                    break;
                }
            }

            for (int i = dataStart; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                processed++;
                try
                {
                    var parts = line.Split(',');
                    if (parts.Length < 6) continue;

                    var trafficDate = DateOnly.Parse(parts[0], CultureInfo.InvariantCulture);
                    var adds = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    var deletes = int.Parse(parts[3], CultureInfo.InvariantCulture);
                    var purchases = int.Parse(parts[4], CultureInfo.InvariantCulture);
                    var gifts = int.Parse(parts[5], CultureInfo.InvariantCulture);

                    var exists = await _db.SteamTraffic.AnyAsync(
                        t => t.GameId == game.Id && t.TrafficDate == trafficDate && t.TrafficSource == "steamworks_wishlist", ct);

                    if (!exists)
                    {
                        _db.SteamTraffic.Add(new SteamTraffic
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            TrafficDate = trafficDate,
                            WishlistAdditions = adds,
                            WishlistDeletions = deletes,
                            Purchases = purchases,
                            TrafficSource = "steamworks_wishlist"
                        });
                        inserted++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Failed to parse wishlist CSV line {Line}", i);
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Wishlist ingestion complete: {Processed} processed, {Inserted} inserted, {Failed} failed",
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

    public async Task<IngestionResult> IngestOwnedGameDataAsync(IngestOwnedGameDataCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "steamworks_owned_game",
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
            var root = doc.RootElement;

            int processed = 1, inserted = 0, failed = 0;

            try
            {
                var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);

                var additions = ParseFormattedInt(root.GetProperty("Wishlist Additions").GetString());
                var deletions = ParseFormattedInt(root.GetProperty("Wishlist Deletions").GetString());
                var purchases = ParseFormattedInt(root.GetProperty("Wishlist Purchases and Activations").GetString());
                var gifts = ParseFormattedInt(root.GetProperty("Wishlist Gifts").GetString());
                var balance = ParseFormattedInt(root.GetProperty("Period Wishlist Balance").GetString());

                var exists = await _db.OwnedGameData.AnyAsync(
                    o => o.GameId == game.Id && o.SnapshotDate == snapshotDate, ct);

                if (!exists)
                {
                    _db.OwnedGameData.Add(new OwnedGameData
                    {
                        Id = Guid.NewGuid(),
                        GameId = game.Id,
                        SnapshotDate = snapshotDate,
                        WishlistAdditions = additions,
                        WishlistDeletions = deletions,
                        PurchasesAndActivations = purchases,
                        Gifts = gifts,
                        PeriodWishlistBalance = balance
                    });
                    inserted = 1;
                }
            }
            catch (Exception ex)
            {
                failed = 1;
                _logger.LogWarning(ex, "Failed to process owned game data");
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Owned game data ingestion complete: {Inserted} inserted", inserted);

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

    public async Task<IngestionResult> IngestGroupMemberCountAsync(IngestGroupMemberCountCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "group_member_count",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FilePath
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            if (!File.Exists(request.FilePath))
                throw new FileNotFoundException($"File not found: {request.FilePath}");

            var json = await File.ReadAllTextAsync(request.FilePath, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Read fields from the Python GroupMemberCount JSON
            // DateTime.SpecifyKind is required — Npgsql rejects DateTimeKind.Unspecified for timestamptz columns
            var scrapedAt = root.TryGetProperty("scraped_at", out var scrapedAtEl)
                ? DateTime.SpecifyKind(scrapedAtEl.GetDateTime(), DateTimeKind.Utc)
                : DateTime.UtcNow;

            var snapshotDate = DateOnly.FromDateTime(scrapedAt);

            int? memberCount = null;
            if (root.TryGetProperty("group_member_count", out var mcEl) &&
                mcEl.ValueKind != JsonValueKind.Null)
                memberCount = mcEl.GetInt32();

            string? error = null;
            if (root.TryGetProperty("error", out var errEl) &&
                errEl.ValueKind != JsonValueKind.Null)
                error = errEl.GetString();

            // Find or create the game
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
            {
                _logger.LogInformation("Game with AppId {AppId} not found. Creating stub and enriching from Steam.", request.AppId);

                game = new Cato.Domain.Entities.Game
                {
                    Id = Guid.NewGuid(),
                    AppId = request.AppId,
                    Name = $"App {request.AppId}",
                    GameType = "Sourcing"
                };
                _db.Games.Add(game);
                await _db.SaveChangesAsync(ct);

                var enrichResult = await _gameService.EnrichGameFromSteamAsync(game.Id, ct);
                if (!enrichResult.IsSuccess)
                    _logger.LogWarning("Steam enrich failed for AppId {AppId}: {Error}", request.AppId, enrichResult.ErrorMessage);
            }

            // Upsert by (GameId, SnapshotDate)
            var existing = await _db.GroupMemberCountSnapshots
                .FirstOrDefaultAsync(s => s.GameId == game.Id && s.SnapshotDate == snapshotDate, ct);

            if (existing is not null)
            {
                if (memberCount.HasValue) existing.MemberCount = memberCount.Value;
                existing.Error = error;
                existing.ScrapedAt = scrapedAt;
            }
            else
            {
                _db.GroupMemberCountSnapshots.Add(new Cato.Domain.Entities.GroupMemberCountSnapshot
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    SnapshotDate = snapshotDate,
                    MemberCount = memberCount ?? 0,
                    Error = error,
                    ScrapedAt = scrapedAt
                });
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = 1;
            log.RecordsInserted = existing is null ? 1 : 0;
            log.RecordsUpdated = existing is not null ? 1 : 0;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Group member count ingestion complete for AppId {AppId}: {Count} members on {Date}",
                request.AppId, memberCount, snapshotDate);

            return new IngestionResult(1, existing is null ? 1 : 0, existing is not null ? 1 : 0, 0, log.Id);
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

    public async Task<List<IngestionLogDto>> GetIngestionLogsAsync(GetIngestionLogsQuery request, CancellationToken ct = default)
    {
        var query = _db.IngestionLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Source))
            query = query.Where(l => l.Source == request.Source);

        return await query
            .OrderByDescending(l => l.StartTime)
            .Take(request.Limit)
            .Select(l => new IngestionLogDto(
                l.Id, l.Source, l.StartTime, l.EndTime, l.Status,
                l.RecordsProcessed, l.RecordsInserted, l.RecordsUpdated,
                l.RecordsFailed, l.ErrorMessage, l.FilePath))
            .ToListAsync(ct);
    }

    /// <summary>Parses numbers formatted with commas like "135,427" → 135427</summary>
    private static int ParseFormattedInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        return int.Parse(value.Replace(",", ""), CultureInfo.InvariantCulture);
    }
}
