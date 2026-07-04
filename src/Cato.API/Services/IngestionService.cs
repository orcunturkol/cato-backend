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
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            var doc = await JsonDocument.ParseAsync(request.Content, cancellationToken: ct);

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

    public async Task<IngestionResult> IngestCcuAsync(IngestCcuCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "steam_current_players",
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

            if (!doc.RootElement.TryGetProperty("response", out var responseEl))
                throw new InvalidOperationException("Unrecognized CCU file format: missing 'response' property");

            processed++;
            try
            {
                var playerCount = responseEl.TryGetProperty("player_count", out var pcEl) ? pcEl.GetInt32() : 0;

                DateTime timestamp;
                if (responseEl.TryGetProperty("timestamp", out var tsEl))
                {
                    timestamp = DateTime.Parse(tsEl.GetString()!, null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal);
                }
                else
                {
                    timestamp = DateTime.UtcNow;
                }

                var exists = await _db.CcuHistories.AnyAsync(
                    c => c.GameId == game.Id && c.Timestamp == timestamp && c.Source == "Steam API", ct);

                if (!exists)
                {
                    _db.CcuHistories.Add(new CcuHistory
                    {
                        Id = Guid.NewGuid(),
                        GameId = game.Id,
                        Timestamp = timestamp,
                        CcuCount = playerCount,
                        Source = "Steam API"
                    });
                    inserted++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Failed to process CCU entry");
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("CCU ingestion complete: {Processed} processed, {Inserted} inserted, {Failed} failed",
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
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
            {
                _logger.LogInformation("Game with AppId {AppId} not found. Creating stub and enriching from Steam.", request.AppId);

                game = new Cato.Domain.Entities.Game
                {
                    Id = Guid.NewGuid(),
                    AppId = request.AppId,
                    Name = $"App {request.AppId}",
                    GameType = "Other"
                };
                _db.Games.Add(game);
                await _db.SaveChangesAsync(ct);

                var enrichResult = await _gameService.EnrichGameFromSteamAsync(game.Id, ct);
                if (!enrichResult.IsSuccess)
                    _logger.LogWarning("Steam enrich failed for AppId {AppId}: {Error}", request.AppId, enrichResult.ErrorMessage);
            }

            var doc = await JsonDocument.ParseAsync(request.Content, cancellationToken: ct);

            int processed = 0, inserted = 0, failed = 0;

            if (doc.RootElement.TryGetProperty("transactions", out var transactions))
            {
                foreach (var txn in transactions.EnumerateArray())
                {
                    processed++;
                    try
                    {
                        var dateStr = txn.GetProperty("date").GetString() ?? string.Empty;
                        var saleDate = DateOnly.ParseExact(
                            dateStr.Replace("/", "-"),
                            ["yyyy-MM-dd", "yyyyMMdd"],
                            System.Globalization.CultureInfo.InvariantCulture);

                        var countryCode = txn.TryGetProperty("country_code", out var ccEl) ? ccEl.GetString() ?? "XX" : "XX";
                        var platform    = txn.TryGetProperty("platform",     out var plEl) && plEl.GetString() is { Length: > 0 } pl ? pl : "Steam";
                        var packageId   = txn.TryGetProperty("packageid",    out var pkEl) && pkEl.ValueKind != JsonValueKind.Null ? pkEl.GetInt32() : (int?)null;

                        var exists = await _db.SteamSaleFinancials.AnyAsync(
                            s => s.GameId == game.Id
                              && s.SaleDate == saleDate
                              && s.CountryCode == countryCode
                              && s.PackageId == packageId
                              && s.Platform == platform, ct);

                        if (!exists)
                        {
                            static decimal ParseMoney(JsonElement el) =>
                                el.ValueKind == JsonValueKind.Null ? 0m
                                : el.ValueKind == JsonValueKind.String ? decimal.TryParse(el.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m
                                : el.GetDecimal();

                            _db.SteamSaleFinancials.Add(new SteamSaleFinancial
                            {
                                Id              = Guid.NewGuid(),
                                GameId          = game.Id,
                                SaleDate        = saleDate,
                                CountryCode     = countryCode,
                                Platform        = platform,
                                PackageId       = packageId,
                                SalesUnits      = txn.TryGetProperty("gross_units_sold",      out var guEl) && guEl.ValueKind != JsonValueKind.Null ? guEl.GetInt32()
                                              : txn.TryGetProperty("gross_units_activated", out var gaEl) && gaEl.ValueKind != JsonValueKind.Null ? gaEl.GetInt32() : 0,
                                ReturnsUnits    = txn.TryGetProperty("gross_units_returned", out var grEl) && grEl.ValueKind != JsonValueKind.Null ? grEl.GetInt32() : 0,
                                GrossRevenueUsd = txn.TryGetProperty("gross_sales_usd",      out var gsEl) ? ParseMoney(gsEl) : 0m,
                                GrossReturnsUsd = txn.TryGetProperty("gross_returns_usd",    out var gRetEl) ? ParseMoney(gRetEl) : 0m,
                                TaxUsd          = txn.TryGetProperty("net_tax_usd",          out var taxEl) ? ParseMoney(taxEl) : 0m,
                                NetRevenueUsd   = txn.TryGetProperty("net_sales_usd",        out var nsEl) ? ParseMoney(nsEl) : 0m,
                                Currency        = txn.TryGetProperty("currency",             out var curEl) ? curEl.GetString() : null,
                                BasePrice       = txn.TryGetProperty("base_price",           out var bpEl)  ? bpEl.GetString()  : null,
                                SalePrice       = txn.TryGetProperty("sale_price",           out var spEl)  ? spEl.GetString()  : null,
                                SaleType        = txn.TryGetProperty("package_sale_type",    out var stEl)  ? stEl.GetString()  : null,
                                CombinedDiscountId    = txn.TryGetProperty("combined_discount_id",         out var cdEl) && cdEl.ValueKind != JsonValueKind.Null ? cdEl.GetInt32() : (int?)null,
                                RevenueShareTier      = txn.TryGetProperty("additional_revenue_share_tier", out var rsEl) && rsEl.ValueKind != JsonValueKind.Null ? rsEl.GetInt32() : (int?)null,
                            });
                            inserted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogWarning(ex, "Failed to process financial transaction in {FileName}", request.FileName);
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
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            using var wishlistCsvReader = new StreamReader(request.Content);
            var wishlistLinesList = new List<string>();
            string? wishlistLn;
            while ((wishlistLn = await wishlistCsvReader.ReadLineAsync(ct)) is not null) wishlistLinesList.Add(wishlistLn);
            var lines = wishlistLinesList.ToArray();

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
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var doc = await JsonDocument.ParseAsync(request.Content, cancellationToken: ct);
            var root = doc.RootElement;

            if (!root.TryGetProperty("game_id", out var gameIdEl) ||
                !int.TryParse(gameIdEl.GetString() ?? gameIdEl.GetRawText().Trim('"'), out var appId))
                throw new InvalidOperationException("File is missing required 'game_id' field.");

            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == appId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {appId} not found. Create the game first.");

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
                    GameType = "Other"
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

    public async Task<IngestionResult> IngestSteamDbSnapshotAsync(IngestSteamDbSnapshotCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "steamdb_snapshot",
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

            var scrapedAt = root.TryGetProperty("scraped_at", out var scrapedAtEl)
                ? DateTime.SpecifyKind(scrapedAtEl.GetDateTime(), DateTimeKind.Utc)
                : DateTime.UtcNow;

            var snapshotDate = DateOnly.FromDateTime(scrapedAt);
            var source = root.GetProperty("source").GetString()!;
            var rank = root.GetProperty("rank").GetInt32();

            var follows = root.TryGetProperty("follows", out var followsEl) && followsEl.ValueKind != JsonValueKind.Null
                ? followsEl.GetInt32() : 0;
            var sevenDayGain = root.TryGetProperty("seven_day_gain", out var gainEl) && gainEl.ValueKind != JsonValueKind.Null
                ? gainEl.GetInt32() : 0;

            string? price = root.TryGetProperty("price", out var priceEl) && priceEl.ValueKind != JsonValueKind.Null
                ? priceEl.GetString() : null;
            string? rating = root.TryGetProperty("rating", out var ratingEl) && ratingEl.ValueKind != JsonValueKind.Null
                ? ratingEl.GetString() : null;
            string? release = root.TryGetProperty("release", out var releaseEl) && releaseEl.ValueKind != JsonValueKind.Null
                ? releaseEl.GetString() : null;

            // Find or create Game
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
            {
                _logger.LogInformation("Game with AppId {AppId} not found. Creating stub and enriching from Steam.", request.AppId);

                string gameName = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind != JsonValueKind.Null
                    ? nameEl.GetString()! : $"App {request.AppId}";

                game = new Game
                {
                    Id = Guid.NewGuid(),
                    AppId = request.AppId,
                    Name = gameName,
                    GameType = "Other"
                };
                _db.Games.Add(game);
                await _db.SaveChangesAsync(ct);

                var enrichResult = await _gameService.EnrichGameFromSteamAsync(game.Id, ct);
                if (!enrichResult.IsSuccess)
                    _logger.LogWarning("Steam enrich failed for AppId {AppId}: {Error}", request.AppId, enrichResult.ErrorMessage);
            }

            // Upsert by (GameId, SnapshotDate, Source)
            var existing = await _db.SteamDbSnapshots
                .FirstOrDefaultAsync(s => s.GameId == game.Id && s.SnapshotDate == snapshotDate && s.Source == source, ct);

            if (existing is not null)
            {
                existing.Rank = rank;
                existing.Follows = follows;
                existing.SevenDayGain = sevenDayGain;
                existing.Price = price;
                existing.Rating = rating;
                existing.Release = release;
                existing.ScrapedAt = scrapedAt;
            }
            else
            {
                _db.SteamDbSnapshots.Add(new SteamDbSnapshot
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    SnapshotDate = snapshotDate,
                    Source = source,
                    Rank = rank,
                    Price = price,
                    Rating = rating,
                    Release = release,
                    Follows = follows,
                    SevenDayGain = sevenDayGain,
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

            _logger.LogInformation("SteamDB snapshot ingestion complete for AppId {AppId}: source={Source}, rank={Rank}",
                request.AppId, source, rank);

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

    public async Task<IngestionResult> IngestSpecialEventsAsync(IngestSpecialEventsCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "steam_special_events",
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
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var now = DateTime.UtcNow;

            // Payload fields are emitted with exclude_none, so every read is a TryGetProperty.
            static string? Str(JsonElement el, string name) =>
                el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            static DateTime? Date(JsonElement el, string name) =>
                el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? DateTime.SpecifyKind(v.GetDateTime().ToUniversalTime(), DateTimeKind.Utc)
                    : null;
            static int? Int(JsonElement el, string name) =>
                el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

            // Hub tab labels, joined to each event's hub_placements for the TabNames jsonb.
            var hubTabLabels = new Dictionary<int, string?>();
            if (root.TryGetProperty("hub_tabs", out var hubTabsEl) && hubTabsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tab in hubTabsEl.EnumerateArray())
                {
                    var id = Int(tab, "unique_id");
                    if (id.HasValue)
                        hubTabLabels[id.Value] = Str(tab, "label");
                }
            }

            int processed = 0, inserted = 0, updated = 0, failed = 0;

            if (root.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var eventEl in eventsEl.EnumerateArray())
                {
                    var gid = Str(eventEl, "announcement_gid");
                    try
                    {
                        if (string.IsNullOrEmpty(gid))
                        {
                            failed++;
                            _logger.LogWarning("Special event without announcement_gid skipped (url={Url}, error={Error})",
                                Str(eventEl, "event_url"), Str(eventEl, "scrape_error"));
                            continue;
                        }

                        processed++;

                        JsonDocument? tabNames = null;
                        if (eventEl.TryGetProperty("hub_placements", out var placementsEl) &&
                            placementsEl.ValueKind == JsonValueKind.Array && placementsEl.GetArrayLength() > 0)
                        {
                            var tabs = placementsEl.EnumerateArray()
                                .Where(p => p.ValueKind == JsonValueKind.Number)
                                .Select(p => p.GetInt32())
                                .Select(id => new { unique_id = id, label = hubTabLabels.GetValueOrDefault(id) })
                                .ToList();
                            tabNames = JsonDocument.Parse(JsonSerializer.Serialize(tabs));
                        }

                        var ev = await _db.SteamSpecialEvents.FirstOrDefaultAsync(e => e.AnnouncementGid == gid, ct);
                        if (ev is null)
                        {
                            ev = new SteamSpecialEvent
                            {
                                Id = Guid.NewGuid(),
                                AnnouncementGid = gid,
                                FirstSeenAt = now
                            };
                            _db.SteamSpecialEvents.Add(ev);
                            inserted++;
                        }
                        else
                        {
                            updated++;
                        }

                        // Steam edits copy/dates after publishing, so mutable fields always refresh.
                        ev.SaleVanityId = Str(eventEl, "sale_vanity_id");
                        ev.EventUrl = Str(eventEl, "event_url") ?? ev.EventUrl;
                        ev.ClanAccountId = Int(eventEl, "clan_accountid") ?? ev.ClanAccountId;
                        ev.EventType = Int(eventEl, "event_type") ?? ev.EventType;
                        ev.Title = Str(eventEl, "title");
                        ev.Subtitle = Str(eventEl, "subtitle");
                        ev.Description = Str(eventEl, "description");
                        ev.HeaderImageUrl = Str(eventEl, "header_image_url");
                        ev.LogoImageUrl = Str(eventEl, "logo_image_url");
                        ev.CapsuleImageUrl = Str(eventEl, "capsule_image_url");
                        ev.BackgroundColor = Str(eventEl, "background_color");
                        ev.StartDate = Date(eventEl, "start_date");
                        ev.EndDate = Date(eventEl, "end_date");
                        if (tabNames is not null)
                            ev.TabNames = tabNames;
                        ev.LastSeenAt = now;

                        await _db.SaveChangesAsync(ct);

                        if (eventEl.TryGetProperty("games", out var gamesEl) && gamesEl.ValueKind == JsonValueKind.Array)
                        {
                            var seenAppIds = new HashSet<int>();
                            foreach (var gameEl in gamesEl.EnumerateArray())
                            {
                                var appId = Int(gameEl, "app_id");
                                var itemType = Str(gameEl, "item_type") ?? "game";
                                // Only apps can become Game rows; bundles/packages stay in the raw file.
                                if (!appId.HasValue || itemType is not ("game" or "demo") || !seenAppIds.Add(appId.Value))
                                    continue;

                                var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == appId.Value, ct);
                                if (game is null)
                                {
                                    _logger.LogInformation("Game with AppId {AppId} not found. Creating stub and enriching from Steam.", appId.Value);
                                    game = new Game
                                    {
                                        Id = Guid.NewGuid(),
                                        AppId = appId.Value,
                                        Name = Str(gameEl, "name") ?? $"App {appId.Value}",
                                        GameType = "Other"
                                    };
                                    _db.Games.Add(game);
                                    await _db.SaveChangesAsync(ct);

                                    var enrichResult = await _gameService.EnrichGameFromSteamAsync(game.Id, ct);
                                    if (!enrichResult.IsSuccess)
                                        _logger.LogWarning("Steam enrich failed for AppId {AppId}: {Error}", appId.Value, enrichResult.ErrorMessage);
                                }

                                var link = await _db.SteamSpecialEventGames
                                    .FirstOrDefaultAsync(l => l.SteamSpecialEventId == ev.Id && l.GameId == game.Id, ct);
                                if (link is null)
                                {
                                    link = new SteamSpecialEventGame
                                    {
                                        Id = Guid.NewGuid(),
                                        SteamSpecialEventId = ev.Id,
                                        GameId = game.Id,
                                        FirstSeenAt = now
                                    };
                                    _db.SteamSpecialEventGames.Add(link);
                                    inserted++;
                                }
                                else
                                {
                                    updated++;
                                }

                                link.ItemType = itemType;
                                link.LastSeenAt = now;
                                // Only overwrite when this scrape actually supplied values.
                                link.SteamDisplayedStart = Date(gameEl, "steam_displayed_start") ?? link.SteamDisplayedStart;
                                link.SteamDisplayedEnd = Date(gameEl, "steam_displayed_end") ?? link.SteamDisplayedEnd;
                                link.DiscountPercent = Int(gameEl, "discount_percent") ?? link.DiscountPercent;
                            }
                        }

                        await _db.SaveChangesAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogWarning(ex, "Failed to ingest special event {Gid}", gid ?? "<no gid>");
                        // Drop any half-staged entities so the next event starts clean,
                        // then re-attach the log row we still need to finalize.
                        _db.ChangeTracker.Clear();
                        _db.IngestionLogs.Attach(log);
                    }
                }
            }

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsUpdated = updated;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Special events ingestion complete: events={Events}, inserted={Inserted}, updated={Updated}, failed={Failed}",
                processed, inserted, updated, failed);

            return new IngestionResult(processed, inserted, updated, failed, log.Id);
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

    public async Task<IngestionResult> IngestRegionalPricesAsync(IngestRegionalPricesCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "regional_price_history",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            var snapshots = await JsonSerializer.DeserializeAsync<List<Dictionary<string, JsonElement>>>(request.Content, cancellationToken: ct)
                ?? throw new InvalidOperationException("Unrecognized regional price file format");

            // Known non-currency keys to skip
            var skipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "timestamp" };

            int processed = 0, inserted = 0, updated = 0, failed = 0;

            foreach (var snapshot in snapshots)
            {
                if (!snapshot.TryGetValue("timestamp", out var tsEl)) continue;
                var capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(tsEl.GetInt64()).UtcDateTime;

                foreach (var (key, value) in snapshot)
                {
                    if (skipKeys.Contains(key)) continue;

                    processed++;
                    try
                    {
                        if (!value.TryGetProperty("fullPrice", out var fullPriceEl) ||
                            !value.TryGetProperty("finalPrice", out var finalPriceEl))
                        {
                            failed++;
                            continue;
                        }

                        var fullPrice = fullPriceEl.GetDecimal();
                        var finalPrice = finalPriceEl.GetDecimal();
                        var discountPct = fullPrice > 0
                            ? (int)Math.Round((fullPrice - finalPrice) / fullPrice * 100)
                            : 0;

                        // BasePriceUsd/FinalPriceUsd store the per-currency price (not always USD)
                        var existing = await _db.PriceSnapshots.FirstOrDefaultAsync(
                            p => p.GameId == game.Id && p.CapturedAt == capturedAt && p.Currency == key, ct);

                        if (existing is null)
                        {
                            _db.PriceSnapshots.Add(new PriceSnapshot
                            {
                                Id = Guid.NewGuid(),
                                GameId = game.Id,
                                CapturedAt = capturedAt,
                                Currency = key,
                                BasePriceUsd = fullPrice,
                                FinalPriceUsd = finalPrice,
                                DiscountPercent = discountPct
                            });
                            inserted++;
                        }
                        else
                        {
                            existing.BasePriceUsd = fullPrice;
                            existing.FinalPriceUsd = finalPrice;
                            existing.DiscountPercent = discountPct;
                            updated++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogWarning(ex, "Failed to process price entry for currency {Currency} at {Timestamp}", key, capturedAt);
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsUpdated = updated;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Regional prices ingestion complete: {Processed} processed, {Inserted} inserted, {Updated} updated, {Failed} failed",
                processed, inserted, updated, failed);

            return new IngestionResult(processed, inserted, updated, failed, log.Id);
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

    public async Task<IngestionResult> IngestWishlistInsightsAsync(IngestWishlistInsightsCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "wishlist_insights",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            var doc = await JsonDocument.ParseAsync(request.Content, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("alsoWishlisted", out var alsoWishlisted))
                throw new InvalidOperationException("Unrecognized wishlist insights format: missing 'alsoWishlisted' property");

            var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);
            int processed = 0, inserted = 0, updated = 0, failed = 0;

            foreach (var entry in alsoWishlisted.EnumerateArray())
            {
                processed++;
                try
                {
                    var steamIdStr = entry.GetProperty("steamId").GetString() ?? "";
                    if (!int.TryParse(steamIdStr, out var relatedAppId))
                    {
                        failed++;
                        continue;
                    }

                    var name = entry.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    var link = entry.TryGetProperty("link", out var linkEl) ? linkEl.GetDecimal() : 0m;
                    var price = entry.TryGetProperty("price", out var priceEl) ? priceEl.GetDecimal() : 0m;
                    var copiesSold = entry.TryGetProperty("copiesSold", out var copiesEl) ? copiesEl.GetInt64() : 0L;
                    var revenue = entry.TryGetProperty("revenue", out var revenueEl) ? revenueEl.GetDecimal() : 0m;

                    DateTime? releaseDate = null;
                    if (entry.TryGetProperty("releaseDate", out var releaseDateEl) && releaseDateEl.ValueKind == JsonValueKind.Number)
                        releaseDate = DateTimeOffset.FromUnixTimeMilliseconds(releaseDateEl.GetInt64()).UtcDateTime;

                    var genres = Array.Empty<string>();
                    if (entry.TryGetProperty("genres", out var genresEl) && genresEl.ValueKind == JsonValueKind.Array)
                        genres = genresEl.EnumerateArray().Select(g => g.GetString() ?? "").ToArray();

                    var existing = await _db.WishlistInsights.FirstOrDefaultAsync(
                        w => w.GameId == game.Id && w.SnapshotDate == snapshotDate && w.RelatedAppId == relatedAppId, ct);

                    if (existing is null)
                    {
                        _db.WishlistInsights.Add(new WishlistInsight
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            SnapshotDate = snapshotDate,
                            RelatedAppId = relatedAppId,
                            RelatedName = name,
                            LinkScore = link,
                            Price = price,
                            ReleaseDate = releaseDate,
                            Genres = genres,
                            CopiesSold = copiesSold,
                            Revenue = revenue
                        });
                        inserted++;
                    }
                    else
                    {
                        existing.RelatedName = name;
                        existing.LinkScore = link;
                        existing.Price = price;
                        existing.ReleaseDate = releaseDate;
                        existing.Genres = genres;
                        existing.CopiesSold = copiesSold;
                        existing.Revenue = revenue;
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Failed to process wishlist insight entry");
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsUpdated = updated;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Wishlist insights ingestion complete: {Processed} processed, {Inserted} inserted, {Updated} updated, {Failed} failed",
                processed, inserted, updated, failed);

            return new IngestionResult(processed, inserted, updated, failed, log.Id);
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

    public async Task<IngestionResult> IngestStoreTrafficAsync(IngestStoreTrafficCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "store_traffic_breakdown",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            using var trafficCsvReader = new StreamReader(request.Content);
            var trafficLinesList = new List<string>();
            string? trafficLn;
            while ((trafficLn = await trafficCsvReader.ReadLineAsync(ct)) is not null) trafficLinesList.Add(trafficLn);
            var lines = trafficLinesList.ToArray();
            var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);
            int processed = 0, inserted = 0, updated = 0, failed = 0;

            // Skip header row
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                processed++;
                try
                {
                    // CSV columns: Page / Category, Page / Feature, Impressions, Visits
                    // Values may be quoted and contain commas (e.g. "12,755")
                    var cols = ParseCsvLine(line);
                    if (cols.Length < 4) { failed++; continue; }

                    var category = cols[0].Trim();
                    var feature = cols[1].Trim();
                    var impressions = ParseFormattedLong(cols[2]);
                    var visits = ParseFormattedLong(cols[3]);

                    var existing = await _db.SteamTrafficBreakdowns.FirstOrDefaultAsync(
                        t => t.GameId == game.Id && t.SnapshotDate == snapshotDate
                             && t.PageCategory == category && t.PageFeature == feature, ct);

                    if (existing is null)
                    {
                        _db.SteamTrafficBreakdowns.Add(new SteamTrafficBreakdown
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            SnapshotDate = snapshotDate,
                            PageCategory = category,
                            PageFeature = feature,
                            Impressions = impressions,
                            Visits = visits
                        });
                        inserted++;
                    }
                    else
                    {
                        existing.Impressions = impressions;
                        existing.Visits = visits;
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Failed to parse store traffic CSV line {Line}", i);
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsUpdated = updated;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Store traffic breakdown ingestion complete: {Processed} processed, {Inserted} inserted, {Updated} updated, {Failed} failed",
                processed, inserted, updated, failed);

            return new IngestionResult(processed, inserted, updated, failed, log.Id);
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

    public async Task<IngestionResult> IngestNewsAsync(IngestNewsCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "game_news",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            var doc = await JsonDocument.ParseAsync(request.Content, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("appnews", out var appnews) ||
                !appnews.TryGetProperty("newsitems", out var newsItems))
                throw new InvalidOperationException("Unrecognized news format: missing 'appnews.newsitems' property.");

            int processed = 0, inserted = 0, updated = 0, failed = 0;

            foreach (var item in newsItems.EnumerateArray())
            {
                processed++;
                try
                {
                    var externalId = item.TryGetProperty("gid", out var gidEl) ? gidEl.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(externalId)) { failed++; continue; }

                    var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
                    var url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
                    var author = item.TryGetProperty("author", out var authorEl) ? authorEl.GetString() : null;
                    var contents = item.TryGetProperty("contents", out var contentsEl) ? contentsEl.GetString() : null;
                    var feedLabel = item.TryGetProperty("feedlabel", out var feedLabelEl) ? feedLabelEl.GetString() : null;
                    var publishedAt = item.TryGetProperty("date", out var dateEl)
                        ? DateTimeOffset.FromUnixTimeSeconds(dateEl.GetInt64()).UtcDateTime
                        : DateTime.UtcNow;

                    var existing = await _db.GameNews.FirstOrDefaultAsync(
                        n => n.GameId == game.Id && n.ExternalId == externalId, ct);

                    if (existing is null)
                    {
                        _db.GameNews.Add(new GameNews
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            ExternalId = externalId,
                            Source = "news",
                            Title = title,
                            Url = url,
                            Author = author,
                            Contents = contents,
                            PublishedAt = publishedAt,
                            FeedLabel = feedLabel
                        });
                        inserted++;
                    }
                    else
                    {
                        existing.Title = title;
                        existing.Url = url;
                        existing.Author = author;
                        existing.Contents = contents;
                        existing.PublishedAt = publishedAt;
                        existing.FeedLabel = feedLabel;
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Failed to process news item");
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsUpdated = updated;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("News ingestion complete: {Processed} processed, {Inserted} inserted, {Updated} updated, {Failed} failed",
                processed, inserted, updated, failed);

            return new IngestionResult(processed, inserted, updated, failed, log.Id);
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

    public async Task<IngestionResult> IngestPatchNotesAsync(IngestPatchNotesCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "patch_notes",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            var doc = await JsonDocument.ParseAsync(request.Content, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("entries", out var entries))
                throw new InvalidOperationException("Unrecognized patch notes format: missing 'entries' property.");

            int processed = 0, inserted = 0, updated = 0, failed = 0;

            foreach (var entry in entries.EnumerateArray())
            {
                processed++;
                try
                {
                    var externalId = entry.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(externalId)) { failed++; continue; }

                    var title = entry.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
                    var url = entry.TryGetProperty("link", out var linkEl) ? linkEl.GetString() : null;
                    var contents = entry.TryGetProperty("summary", out var summaryEl) ? summaryEl.GetString() : null;

                    DateTime publishedAt = DateTime.UtcNow;
                    if (entry.TryGetProperty("published", out var publishedEl))
                    {
                        var publishedStr = publishedEl.GetString() ?? "";
                        if (!string.IsNullOrEmpty(publishedStr))
                        {
                            if (!DateTime.TryParseExact(publishedStr, "ddd, dd MMM yyyy HH:mm:ss zzz",
                                    CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out publishedAt))
                            {
                                DateTime.TryParse(publishedStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.AdjustToUniversal, out publishedAt);
                            }
                        }
                    }

                    var existing = await _db.GameNews.FirstOrDefaultAsync(
                        n => n.GameId == game.Id && n.ExternalId == externalId, ct);

                    if (existing is null)
                    {
                        _db.GameNews.Add(new GameNews
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            ExternalId = externalId,
                            Source = "patch_notes",
                            Title = title,
                            Url = url,
                            Author = null,
                            Contents = contents,
                            PublishedAt = publishedAt,
                            FeedLabel = "Patch Notes"
                        });
                        inserted++;
                    }
                    else
                    {
                        existing.Title = title;
                        existing.Url = url;
                        existing.Contents = contents;
                        existing.PublishedAt = publishedAt;
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Failed to process patch notes entry");
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsUpdated = updated;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Patch notes ingestion complete: {Processed} processed, {Inserted} inserted, {Updated} updated, {Failed} failed",
                processed, inserted, updated, failed);

            return new IngestionResult(processed, inserted, updated, failed, log.Id);
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

    public async Task<IngestionResult> IngestActiveUsersHistoryAsync(IngestActiveUsersHistoryCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "active_users_history",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            var doc = await JsonDocument.ParseAsync(request.Content, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("active_users_history", out var historyArray))
                throw new InvalidOperationException("Unrecognized format: missing 'active_users_history' property.");

            int processed = 0, inserted = 0, updated = 0, failed = 0;

            foreach (var entry in historyArray.EnumerateArray())
            {
                processed++;
                try
                {
                    if (!entry.TryGetProperty("timestamp", out var tsEl)) { failed++; continue; }
                    var recordedAt = DateTimeOffset.FromUnixTimeMilliseconds(tsEl.GetInt64()).UtcDateTime;
                    var dau = entry.TryGetProperty("dau", out var dauEl) ? dauEl.GetInt32() : 0;
                    var mau = entry.TryGetProperty("mau", out var mauEl) ? mauEl.GetInt32() : 0;

                    var existing = await _db.ActiveUsersHistories.FirstOrDefaultAsync(
                        a => a.GameId == game.Id && a.RecordedAt == recordedAt, ct);

                    if (existing is null)
                    {
                        _db.ActiveUsersHistories.Add(new ActiveUsersHistory
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            RecordedAt = recordedAt,
                            Dau = dau,
                            Mau = mau
                        });
                        inserted++;
                    }
                    else
                    {
                        existing.Dau = dau;
                        existing.Mau = mau;
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Failed to process active users history entry");
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsUpdated = updated;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Active users history ingestion complete: {Processed} processed, {Inserted} inserted, {Updated} updated, {Failed} failed",
                processed, inserted, updated, failed);

            return new IngestionResult(processed, inserted, updated, failed, log.Id);
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

    public async Task<IngestionResult> IngestDemoDownloadsAsync(IngestDemoDownloadsCommand request, CancellationToken ct = default)
    {
        var log = new IngestionLog
        {
            Id = Guid.NewGuid(),
            Source = "demo_downloads",
            StartTime = DateTime.UtcNow,
            Status = "Running",
            FilePath = request.FileName
        };
        _db.IngestionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        try
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
            if (game is null)
                throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

            using var reader = new StreamReader(request.Content);
            var allLines = new List<string>();
            string? ln;
            while ((ln = await reader.ReadLineAsync(ct)) is not null) allLines.Add(ln);

            var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);
            int processed = 0, inserted = 0, updated = 0, failed = 0;

            // Skip header; first section = Region rows, switch to Country after separator "Country,0[,]"
            var geoType = "Region";
            for (int i = 1; i < allLines.Count; i++)
            {
                var line = allLines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var cols = ParseCsvLine(line);
                if (cols.Length < 2) continue;

                var geoName = cols[0].Trim();
                var rawDownloads = cols[1].Trim();

                // Detect section separator: "Country,0," row
                if (geoName == "Country" && rawDownloads == "0")
                {
                    geoType = "Country";
                    continue;
                }

                processed++;
                try
                {
                    var totalDownloads = ParseFormattedLong(rawDownloads);
                    decimal? sharePercent = null;
                    if (cols.Length >= 3 && !string.IsNullOrWhiteSpace(cols[2]))
                    {
                        var shareStr = cols[2].Trim().TrimEnd('%');
                        if (decimal.TryParse(shareStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var share))
                            sharePercent = share;
                    }

                    var existing = await _db.DemoDownloads.FirstOrDefaultAsync(
                        d => d.GameId == game.Id && d.SnapshotDate == snapshotDate
                             && d.GeoType == geoType && d.GeoName == geoName, ct);

                    if (existing is null)
                    {
                        _db.DemoDownloads.Add(new DemoDownload
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            DemoAppId = request.DemoAppId,
                            SnapshotDate = snapshotDate,
                            GeoType = geoType,
                            GeoName = geoName,
                            TotalDownloads = totalDownloads,
                            SharePercent = sharePercent
                        });
                        inserted++;
                    }
                    else
                    {
                        existing.TotalDownloads = totalDownloads;
                        existing.SharePercent = sharePercent;
                        if (request.DemoAppId.HasValue) existing.DemoAppId = request.DemoAppId;
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex, "Failed to parse demo downloads CSV line {Line}", i);
                }
            }

            await _db.SaveChangesAsync(ct);

            log.EndTime = DateTime.UtcNow;
            log.Status = "Completed";
            log.RecordsProcessed = processed;
            log.RecordsInserted = inserted;
            log.RecordsUpdated = updated;
            log.RecordsFailed = failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Demo downloads ingestion complete: {Processed} processed, {Inserted} inserted, {Updated} updated, {Failed} failed",
                processed, inserted, updated, failed);

            return new IngestionResult(processed, inserted, updated, failed, log.Id);
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

    // ─────────────────────────────────────────────────────────────────────
    //  Batch-item mode: consumed by BatchIngestionDispatcher.
    //  Stages entities on the shared DbContext and returns counts.
    //  The dispatcher owns the transaction and the single SaveChangesAsync
    //  call at the end of the batch. Stub-game creation issues its own
    //  SaveChangesAsync (inside the caller's transaction) so navigation
    //  identity is available before enrichment.
    // ─────────────────────────────────────────────────────────────────────

    public async Task<ItemIngestResult> IngestCcuItemAsync(int appId, DateTimeOffset scrapedAt, JsonElement data, CancellationToken ct = default)
    {
        var game = await FindOrStubGameAsync(appId, null, ct);

        if (!data.TryGetProperty("response", out var responseEl))
            return new ItemIngestResult(1, 0, 0, 1);

        var playerCount = responseEl.TryGetProperty("player_count", out var pcEl) && pcEl.ValueKind == JsonValueKind.Number
            ? pcEl.GetInt32() : 0;

        var timestamp = responseEl.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.String
            ? DateTime.Parse(tsEl.GetString()!, null, DateTimeStyles.AdjustToUniversal)
            : scrapedAt.UtcDateTime;

        var exists = await _db.CcuHistories.AnyAsync(
            c => c.GameId == game.Id && c.Timestamp == timestamp && c.Source == "Steam API", ct);

        if (exists) return new ItemIngestResult(1, 0, 0, 0);

        _db.CcuHistories.Add(new CcuHistory
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Timestamp = timestamp,
            CcuCount = playerCount,
            Source = "Steam API"
        });
        return new ItemIngestResult(1, 1, 0, 0);
    }

    public async Task<ItemIngestResult> IngestGroupMemberCountItemAsync(int appId, DateTimeOffset scrapedAt, JsonElement data, CancellationToken ct = default)
    {
        var game = await FindOrStubGameAsync(appId, null, ct);

        var itemScrapedAt = data.TryGetProperty("scraped_at", out var scrapedAtEl) && scrapedAtEl.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(scrapedAtEl.GetDateTime(), DateTimeKind.Utc)
            : scrapedAt.UtcDateTime;
        var snapshotDate = DateOnly.FromDateTime(itemScrapedAt);

        int? memberCount = null;
        if (data.TryGetProperty("group_member_count", out var mcEl) && mcEl.ValueKind == JsonValueKind.Number)
            memberCount = mcEl.GetInt32();

        string? error = data.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
            ? errEl.GetString() : null;

        var existing = await _db.GroupMemberCountSnapshots
            .FirstOrDefaultAsync(s => s.GameId == game.Id && s.SnapshotDate == snapshotDate, ct);

        if (existing is not null)
        {
            if (memberCount.HasValue) existing.MemberCount = memberCount.Value;
            existing.Error = error;
            existing.ScrapedAt = itemScrapedAt;
            return new ItemIngestResult(1, 0, 1, 0);
        }

        _db.GroupMemberCountSnapshots.Add(new GroupMemberCountSnapshot
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            SnapshotDate = snapshotDate,
            MemberCount = memberCount ?? 0,
            Error = error,
            ScrapedAt = itemScrapedAt
        });
        return new ItemIngestResult(1, 1, 0, 0);
    }

    public async Task<ItemIngestResult> IngestSteamDbSnapshotItemAsync(int appId, DateTimeOffset scrapedAt, JsonElement data, CancellationToken ct = default)
    {
        string? stubName = data.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString() : null;
        var game = await FindOrStubGameAsync(appId, stubName, ct);

        var itemScrapedAt = data.TryGetProperty("scraped_at", out var scrapedAtEl) && scrapedAtEl.ValueKind == JsonValueKind.String
            ? DateTime.SpecifyKind(scrapedAtEl.GetDateTime(), DateTimeKind.Utc)
            : scrapedAt.UtcDateTime;
        var snapshotDate = DateOnly.FromDateTime(itemScrapedAt);
        var source = data.GetProperty("source").GetString()!;
        var rank = data.GetProperty("rank").GetInt32();

        var follows = data.TryGetProperty("follows", out var followsEl) && followsEl.ValueKind == JsonValueKind.Number
            ? followsEl.GetInt32() : 0;
        var sevenDayGain = data.TryGetProperty("seven_day_gain", out var gainEl) && gainEl.ValueKind == JsonValueKind.Number
            ? gainEl.GetInt32() : 0;

        string? price = data.TryGetProperty("price", out var priceEl) && priceEl.ValueKind == JsonValueKind.String ? priceEl.GetString() : null;
        string? rating = data.TryGetProperty("rating", out var ratingEl) && ratingEl.ValueKind == JsonValueKind.String ? ratingEl.GetString() : null;
        string? release = data.TryGetProperty("release", out var releaseEl) && releaseEl.ValueKind == JsonValueKind.String ? releaseEl.GetString() : null;

        var existing = await _db.SteamDbSnapshots
            .FirstOrDefaultAsync(s => s.GameId == game.Id && s.SnapshotDate == snapshotDate && s.Source == source, ct);

        if (existing is not null)
        {
            existing.Rank = rank;
            existing.Follows = follows;
            existing.SevenDayGain = sevenDayGain;
            existing.Price = price;
            existing.Rating = rating;
            existing.Release = release;
            existing.ScrapedAt = itemScrapedAt;
            return new ItemIngestResult(1, 0, 1, 0);
        }

        _db.SteamDbSnapshots.Add(new SteamDbSnapshot
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            SnapshotDate = snapshotDate,
            Source = source,
            Rank = rank,
            Price = price,
            Rating = rating,
            Release = release,
            Follows = follows,
            SevenDayGain = sevenDayGain,
            ScrapedAt = itemScrapedAt
        });
        return new ItemIngestResult(1, 1, 0, 0);
    }

    public async Task<ItemIngestResult> IngestFinancialDataItemAsync(int appId, DateTimeOffset scrapedAt, JsonElement data, CancellationToken ct = default)
    {
        var game = await FindOrStubGameAsync(appId, null, ct);

        int processed = 0, inserted = 0, failed = 0;

        if (!data.TryGetProperty("transactions", out var transactions))
            return new ItemIngestResult(0, 0, 0, 0);

        foreach (var txn in transactions.EnumerateArray())
        {
            processed++;
            try
            {
                var dateStr = txn.GetProperty("date").GetString() ?? string.Empty;
                var saleDate = DateOnly.ParseExact(
                    dateStr.Replace("/", "-"),
                    ["yyyy-MM-dd", "yyyyMMdd"],
                    CultureInfo.InvariantCulture);

                var countryCode = txn.TryGetProperty("country_code", out var ccEl) ? ccEl.GetString() ?? "XX" : "XX";
                var platform    = txn.TryGetProperty("platform",     out var plEl) && plEl.GetString() is { Length: > 0 } pl ? pl : "Steam";
                var packageId   = txn.TryGetProperty("packageid",    out var pkEl) && pkEl.ValueKind != JsonValueKind.Null ? pkEl.GetInt32() : (int?)null;

                var exists = await _db.SteamSaleFinancials.AnyAsync(
                    s => s.GameId == game.Id
                      && s.SaleDate == saleDate
                      && s.CountryCode == countryCode
                      && s.PackageId == packageId
                      && s.Platform == platform, ct);

                if (exists) continue;

                static decimal ParseMoney(JsonElement el) =>
                    el.ValueKind == JsonValueKind.Null ? 0m
                    : el.ValueKind == JsonValueKind.String ? decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m
                    : el.GetDecimal();

                _db.SteamSaleFinancials.Add(new SteamSaleFinancial
                {
                    Id              = Guid.NewGuid(),
                    GameId          = game.Id,
                    SaleDate        = saleDate,
                    CountryCode     = countryCode,
                    Platform        = platform,
                    PackageId       = packageId,
                    SalesUnits      = txn.TryGetProperty("gross_units_sold",      out var guEl)  && guEl.ValueKind  != JsonValueKind.Null ? guEl.GetInt32()
                                    : txn.TryGetProperty("gross_units_activated", out var gaEl) && gaEl.ValueKind != JsonValueKind.Null ? gaEl.GetInt32() : 0,
                    ReturnsUnits    = txn.TryGetProperty("gross_units_returned",  out var grEl) && grEl.ValueKind != JsonValueKind.Null ? grEl.GetInt32() : 0,
                    GrossRevenueUsd = txn.TryGetProperty("gross_sales_usd",       out var gsEl)   ? ParseMoney(gsEl)   : 0m,
                    GrossReturnsUsd = txn.TryGetProperty("gross_returns_usd",     out var gRetEl) ? ParseMoney(gRetEl) : 0m,
                    TaxUsd          = txn.TryGetProperty("net_tax_usd",           out var taxEl)  ? ParseMoney(taxEl)  : 0m,
                    NetRevenueUsd   = txn.TryGetProperty("net_sales_usd",         out var nsEl)   ? ParseMoney(nsEl)   : 0m,
                    Currency        = txn.TryGetProperty("currency",              out var curEl)  ? curEl.GetString()  : null,
                    BasePrice       = txn.TryGetProperty("base_price",            out var bpEl)   ? bpEl.GetString()   : null,
                    SalePrice       = txn.TryGetProperty("sale_price",            out var spEl)   ? spEl.GetString()   : null,
                    SaleType        = txn.TryGetProperty("package_sale_type",     out var stEl)   ? stEl.GetString()   : null,
                    CombinedDiscountId    = txn.TryGetProperty("combined_discount_id",         out var cdEl) && cdEl.ValueKind != JsonValueKind.Null ? cdEl.GetInt32() : (int?)null,
                    RevenueShareTier      = txn.TryGetProperty("additional_revenue_share_tier", out var rsEl) && rsEl.ValueKind != JsonValueKind.Null ? rsEl.GetInt32() : (int?)null,
                });
                inserted++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Failed to process financial transaction (appId={AppId})", appId);
            }
        }

        return new ItemIngestResult(processed, inserted, 0, failed);
    }

    private async Task<Game> FindOrStubGameAsync(int appId, string? stubName, CancellationToken ct)
    {
        var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == appId, ct);
        if (game is not null) return game;

        _logger.LogInformation("Game with AppId {AppId} not found. Creating stub and enriching from Steam.", appId);

        game = new Game
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            Name = !string.IsNullOrWhiteSpace(stubName) ? stubName : $"App {appId}",
            GameType = "Other"
        };
        _db.Games.Add(game);
        await _db.SaveChangesAsync(ct);

        var enrichResult = await _gameService.EnrichGameFromSteamAsync(game.Id, ct);
        if (!enrichResult.IsSuccess)
            _logger.LogWarning("Steam enrich failed for AppId {AppId}: {Error}", appId, enrichResult.ErrorMessage);

        return game;
    }

    /// <summary>Parses numbers formatted with commas like "135,427" → 135427</summary>
    private static int ParseFormattedInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        return int.Parse(value.Replace(",", ""), CultureInfo.InvariantCulture);
    }

    /// <summary>Parses long integers formatted with commas like "1,497,394" → 1497394</summary>
    private static long ParseFormattedLong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        return long.Parse(value.Trim().Replace(",", ""), CultureInfo.InvariantCulture);
    }

    /// <summary>Splits a CSV line respecting double-quoted fields that may contain commas.</summary>
    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
