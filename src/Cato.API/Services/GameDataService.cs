using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Models.SteamDb;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services;

public class GameDataService : IGameDataService
{
    private readonly CatoDbContext _db;

    public GameDataService(CatoDbContext db) => _db = db;

    public async Task<List<CcuDto>> GetCcuHistoryAsync(GetCcuHistoryQuery request, CancellationToken ct = default)
    {
        var query = _db.CcuHistories
            .AsNoTracking()
            .Include(c => c.Game)
            .Where(c => c.Game.AppId == request.AppId);

        if (!string.IsNullOrWhiteSpace(request.Source))
            query = query.Where(c => c.Source == request.Source);

        return await query
            .OrderByDescending(c => c.Timestamp)
            .Take(request.Limit)
            .Select(c => new CcuDto(c.Id, c.Timestamp, c.CcuCount, c.Source))
            .ToListAsync(ct);
    }

    public async Task<List<FinancialDto>> GetFinancialDataAsync(GetFinancialDataQuery request, CancellationToken ct = default)
    {
        var query = _db.SteamSaleFinancials
            .AsNoTracking()
            .Include(f => f.Game)
            .Where(f => f.Game.AppId == request.AppId);

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
            query = query.Where(f => f.CountryCode == request.CountryCode);

        return await query
            .OrderByDescending(f => f.SaleDate)
            .Take(request.Limit)
            .Select(f => new FinancialDto(
                f.Id, f.SaleDate, f.CountryCode,
                f.SalesUnits, f.ReturnsUnits, f.NetUnits,
                f.GrossRevenueUsd, f.NetRevenueUsd,
                f.SaleType, f.Platform))
            .ToListAsync(ct);
    }

    public async Task<List<TrafficDto>> GetTrafficDataAsync(GetTrafficDataQuery request, CancellationToken ct = default)
    {
        var query = _db.SteamTraffic
            .AsNoTracking()
            .Include(t => t.Game)
            .Where(t => t.Game.AppId == request.AppId);

        if (!string.IsNullOrWhiteSpace(request.Source))
            query = query.Where(t => t.TrafficSource == request.Source);

        return await query
            .OrderByDescending(t => t.TrafficDate)
            .Take(request.Limit)
            .Select(t => new TrafficDto(
                t.Id, t.TrafficDate,
                t.WishlistAdditions, t.WishlistDeletions, t.NetWishlistChange,
                t.Purchases, t.TrafficSource, t.PurchaseConversionRate))
            .ToListAsync(ct);
    }

    public async Task<List<OwnedGameDto>> GetOwnedGameDataAsync(GetOwnedGameDataQuery request, CancellationToken ct = default)
    {
        return await _db.OwnedGameData
            .AsNoTracking()
            .Include(o => o.Game)
            .Where(o => o.Game.AppId == request.AppId)
            .OrderByDescending(o => o.SnapshotDate)
            .Take(request.Limit)
            .Select(o => new OwnedGameDto(
                o.Id, o.SnapshotDate,
                o.WishlistAdditions, o.WishlistDeletions,
                o.PurchasesAndActivations, o.Gifts,
                o.PeriodWishlistBalance))
            .ToListAsync(ct);
    }

    public async Task<List<SteamworksWishlistDailyDto>> GetSteamworksWishlistDailyAsync(GetSteamworksWishlistDailyQuery request, CancellationToken ct = default)
    {
        var query = _db.SteamworksWishlistDaily
            .AsNoTracking()
            .Where(d => d.Game.AppId == request.AppId);

        if (request.From is { } from) query = query.Where(d => d.DateLocal >= from);
        if (request.To is { } to) query = query.Where(d => d.DateLocal <= to);

        return await query
            .OrderBy(d => d.DateLocal)
            .Select(d => new SteamworksWishlistDailyDto(
                d.DateLocal, d.Adds, d.Deletes, d.PurchasesAndActivations, d.Gifts))
            .ToListAsync(ct);
    }

    public async Task<List<SteamworksWishlistCohortDto>> GetSteamworksWishlistCohortsAsync(GetSteamworksWishlistCohortsQuery request, CancellationToken ct = default)
    {
        var query = _db.SteamworksWishlistCohorts
            .AsNoTracking()
            .Where(c => c.Game.AppId == request.AppId);

        if (request.From is { } from) query = query.Where(c => c.DateLocal >= from);
        if (request.To is { } to) query = query.Where(c => c.DateLocal <= to);

        return await query
            .OrderBy(c => c.DateLocal)
            .ThenBy(c => c.MonthCohort)
            .Select(c => new SteamworksWishlistCohortDto(
                c.DateLocal, c.MonthCohort, c.PurchasesAndActivations, c.Gifts, c.TotalConversions))
            .ToListAsync(ct);
    }

    public async Task<SteamworksWishlistCountriesDto> GetSteamworksWishlistCountriesAsync(GetSteamworksWishlistCountriesQuery request, CancellationToken ct = default)
    {
        var all = _db.SteamworksWishlistCountries
            .AsNoTracking()
            .Where(c => c.Game.AppId == request.AppId);

        var firstDate = await all.MinAsync(c => (DateOnly?)c.DateLocal, ct);
        var lastDate = await all.MaxAsync(c => (DateOnly?)c.DateLocal, ct);

        var query = all;
        if (request.From is { } from) query = query.Where(c => c.DateLocal >= from);
        if (request.To is { } to) query = query.Where(c => c.DateLocal <= to);

        // Grouped by name only: a country's region could change between exports,
        // and splitting one country into two rows over that would be wrong.
        var totals = await query
            .GroupBy(c => c.Country)
            .Select(g => new
            {
                Country = g.Key,
                Region = g.Max(c => c.Region),
                Adds = g.Sum(c => c.Adds),
                Deletes = g.Sum(c => c.Deletes),
                Purchases = g.Sum(c => c.PurchasesAndActivations),
                Gifts = g.Sum(c => c.Gifts),
            })
            .ToListAsync(ct);

        var countries = totals
            .Select(t => new SteamworksWishlistCountryTotalDto(t.Country, t.Region ?? string.Empty, t.Adds, t.Deletes, t.Purchases, t.Gifts))
            .OrderByDescending(t => t.Adds)
            .ThenBy(t => t.Country, StringComparer.Ordinal)
            .ToList();

        return new SteamworksWishlistCountriesDto(firstDate, lastDate, countries);
    }

    public async Task<SteamworksWishlistSummaryDto> GetSteamworksWishlistSummaryAsync(CancellationToken ct = default)
    {
        var latest = await _db.SteamworksWishlistDaily.MaxAsync(d => (DateOnly?)d.DateLocal, ct);
        if (latest is null)
            return new SteamworksWishlistSummaryDto(null, []);

        // Same inclusive 30-day window as the per-game view: the latest day and the 29 before it.
        var from30 = latest.Value.AddDays(-29);

        var totals = await _db.SteamworksWishlistDaily
            .AsNoTracking()
            .GroupBy(d => d.GameId)
            .Select(g => new
            {
                GameId = g.Key,
                FirstDate = g.Min(d => d.DateLocal),
                LastDate = g.Max(d => d.DateLocal),
                Adds = g.Sum(d => d.Adds),
                Deletes = g.Sum(d => d.Deletes),
                Purchases = g.Sum(d => d.PurchasesAndActivations),
                Gifts = g.Sum(d => d.Gifts),
                Adds30d = g.Sum(d => d.DateLocal >= from30 ? d.Adds : 0),
                Deletes30d = g.Sum(d => d.DateLocal >= from30 ? d.Deletes : 0),
                Conversions30d = g.Sum(d => d.DateLocal >= from30 ? d.PurchasesAndActivations + d.Gifts : 0),
            })
            .ToListAsync(ct);

        var gameIds = totals.Select(t => t.GameId).ToList();
        var games = await _db.Games
            .AsNoTracking()
            .Where(g => gameIds.Contains(g.Id))
            .Select(g => new { g.Id, g.AppId, g.Name, g.GameType, g.HeaderImageUrl })
            .ToDictionaryAsync(g => g.Id, ct);

        var rows = totals
            .Where(t => games.ContainsKey(t.GameId))
            .Select(t =>
            {
                var game = games[t.GameId];
                return new SteamworksWishlistGameSummaryDto(
                    t.GameId, game.AppId, game.Name, game.GameType, game.HeaderImageUrl,
                    t.FirstDate, t.LastDate,
                    t.Adds, t.Deletes, t.Purchases, t.Gifts,
                    t.Adds - t.Deletes - t.Purchases - t.Gifts,
                    t.Adds30d, t.Deletes30d, t.Conversions30d);
            })
            .OrderByDescending(r => r.Outstanding)
            .ToList();

        return new SteamworksWishlistSummaryDto(latest, rows);
    }

    public async Task<List<SalesDailyDto>> GetSalesDailyAsync(GetSalesDailyQuery request, CancellationToken ct = default)
    {
        var query = _db.SteamSaleFinancials
            .AsNoTracking()
            .Where(s => s.Game.AppId == request.AppId);

        if (request.From is { } from) query = query.Where(s => s.SaleDate >= from);
        if (request.To is { } to) query = query.Where(s => s.SaleDate <= to);

        var days = await query
            .GroupBy(s => s.SaleDate)
            .Select(g => new
            {
                Date = g.Key,
                Gross = g.Sum(s => s.SalesUnits),
                Returned = g.Sum(s => s.ReturnsUnits),
                Activated = g.Sum(s => s.ActivatedUnits),
                GrossUsd = g.Sum(s => s.GrossRevenueUsd),
                ReturnsUsd = g.Sum(s => s.GrossReturnsUsd),
                NetUsd = g.Sum(s => s.NetRevenueUsd),
            })
            .ToListAsync(ct);

        return days
            .OrderBy(d => d.Date)
            .Select(d => new SalesDailyDto(d.Date, d.Gross, d.Returned, d.Gross - d.Returned, d.Activated, d.GrossUsd, d.ReturnsUsd, d.NetUsd))
            .ToList();
    }

    public async Task<SalesCountriesDto> GetSalesCountriesAsync(GetSalesCountriesQuery request, CancellationToken ct = default)
    {
        var all = _db.SteamSaleFinancials
            .AsNoTracking()
            .Where(s => s.Game.AppId == request.AppId);

        var firstDate = await all.MinAsync(s => (DateOnly?)s.SaleDate, ct);
        var lastDate = await all.MaxAsync(s => (DateOnly?)s.SaleDate, ct);

        var query = all;
        if (request.From is { } from) query = query.Where(s => s.SaleDate >= from);
        if (request.To is { } to) query = query.Where(s => s.SaleDate <= to);

        var totals = await query
            .GroupBy(s => s.CountryCode)
            .Select(g => new
            {
                Code = g.Key,
                Gross = g.Sum(s => s.SalesUnits),
                Returned = g.Sum(s => s.ReturnsUnits),
                GrossUsd = g.Sum(s => s.GrossRevenueUsd),
                NetUsd = g.Sum(s => s.NetRevenueUsd),
            })
            .ToListAsync(ct);

        var codes = totals.Select(t => t.Code).ToList();
        var countries = await _db.SteamCountries
            .AsNoTracking()
            .Where(c => codes.Contains(c.CountryCode))
            .ToDictionaryAsync(c => c.CountryCode, ct);

        var rows = totals
            .Select(t =>
            {
                countries.TryGetValue(t.Code, out var country);
                return new SalesCountryDto(
                    t.Code,
                    country?.CountryName ?? t.Code,
                    country?.Region ?? string.Empty,
                    t.Gross, t.Returned, t.Gross - t.Returned, t.GrossUsd, t.NetUsd);
            })
            .OrderByDescending(r => r.NetUsd)
            .ThenBy(r => r.CountryCode, StringComparer.Ordinal)
            .ToList();

        return new SalesCountriesDto(firstDate, lastDate, rows);
    }

    public async Task<SalesSummaryDto> GetSalesSummaryAsync(CancellationToken ct = default)
    {
        var latest = await _db.SteamSaleFinancials.MaxAsync(s => (DateOnly?)s.SaleDate, ct);
        if (latest is null)
            return new SalesSummaryDto(null, []);

        // Same inclusive 30-day window as the wishlist summary.
        var from30 = latest.Value.AddDays(-29);

        var totals = await _db.SteamSaleFinancials
            .AsNoTracking()
            .GroupBy(s => s.GameId)
            .Select(g => new
            {
                GameId = g.Key,
                FirstDate = g.Min(s => s.SaleDate),
                LastDate = g.Max(s => s.SaleDate),
                Gross = g.Sum(s => s.SalesUnits),
                Returned = g.Sum(s => s.ReturnsUnits),
                Activated = g.Sum(s => s.ActivatedUnits),
                GrossUsd = g.Sum(s => s.GrossRevenueUsd),
                NetUsd = g.Sum(s => s.NetRevenueUsd),
                NetUnits30d = g.Sum(s => s.SaleDate >= from30 ? s.SalesUnits - s.ReturnsUnits : 0),
                NetUsd30d = g.Sum(s => s.SaleDate >= from30 ? s.NetRevenueUsd : 0m),
            })
            .ToListAsync(ct);

        var gameIds = totals.Select(t => t.GameId).ToList();
        var games = await _db.Games
            .AsNoTracking()
            .Where(g => gameIds.Contains(g.Id))
            .Select(g => new { g.Id, g.AppId, g.Name, g.GameType, g.HeaderImageUrl })
            .ToDictionaryAsync(g => g.Id, ct);

        var rows = totals
            .Where(t => games.ContainsKey(t.GameId))
            .Select(t =>
            {
                var game = games[t.GameId];
                return new SalesGameSummaryDto(
                    t.GameId, game.AppId, game.Name, game.GameType, game.HeaderImageUrl,
                    t.FirstDate, t.LastDate,
                    t.Gross, t.Returned, t.Gross - t.Returned, t.Activated,
                    t.GrossUsd, t.NetUsd, t.NetUnits30d, t.NetUsd30d);
            })
            .OrderByDescending(r => r.NetUsd)
            .ToList();

        return new SalesSummaryDto(latest, rows);
    }

    // Store traffic CSV categories that are not page sources (see SteamTrafficBreakdown).
    private const string TrafficCountryCategory = "Country";
    private const string TrafficBotCategory = "Bot Traffic";

    private IQueryable<SteamTrafficBreakdown> StoreTrafficRows(int appId, DateOnly? from, DateOnly? to)
    {
        var query = _db.SteamTrafficBreakdowns.AsNoTracking().Where(t => t.Game.AppId == appId);
        if (from is { } f) query = query.Where(t => t.SnapshotDate >= f);
        if (to is { } e) query = query.Where(t => t.PeriodEnd <= e);
        return query;
    }

    public async Task<List<StoreTrafficPeriodDto>> GetStoreTrafficPeriodsAsync(GetStoreTrafficPeriodsQuery request, CancellationToken ct = default)
    {
        var periods = await StoreTrafficRows(request.AppId, request.From, request.To)
            .Where(t => t.PageCategory != TrafficCountryCategory)
            .GroupBy(t => new { t.SnapshotDate, t.PeriodEnd })
            .Select(g => new
            {
                g.Key.SnapshotDate,
                g.Key.PeriodEnd,
                Impressions = g.Sum(t => t.Impressions),
                Visits = g.Sum(t => t.PageCategory == TrafficBotCategory ? 0 : t.Visits),
                BotVisits = g.Sum(t => t.PageCategory == TrafficBotCategory ? t.Visits : 0),
                OwnerImpressions = g.Sum(t => t.OwnerImpressions),
                OwnerVisits = g.Sum(t => t.PageCategory == TrafficBotCategory ? 0 : t.OwnerVisits),
            })
            .ToListAsync(ct);

        return periods
            .OrderBy(p => p.SnapshotDate)
            .Select(p => new StoreTrafficPeriodDto(
                p.SnapshotDate, p.PeriodEnd, p.Impressions, p.Visits, p.BotVisits, p.OwnerImpressions, p.OwnerVisits))
            .ToList();
    }

    public async Task<StoreTrafficBreakdownDto> GetStoreTrafficBreakdownAsync(GetStoreTrafficBreakdownQuery request, CancellationToken ct = default)
    {
        var all = StoreTrafficRows(request.AppId, null, null);
        var firstDate = await all.MinAsync(t => (DateOnly?)t.SnapshotDate, ct);
        var lastDate = await all.MaxAsync(t => (DateOnly?)t.PeriodEnd, ct);
        var firstDailyDate = await all.Where(t => t.SnapshotDate == t.PeriodEnd).MinAsync(t => (DateOnly?)t.SnapshotDate, ct);

        var grouped = await StoreTrafficRows(request.AppId, request.From, request.To)
            .GroupBy(t => new { t.PageCategory, t.PageFeature })
            .Select(g => new
            {
                g.Key.PageCategory,
                g.Key.PageFeature,
                Impressions = g.Sum(t => t.Impressions),
                Visits = g.Sum(t => t.Visits),
                OwnerImpressions = g.Sum(t => t.OwnerImpressions),
                OwnerVisits = g.Sum(t => t.OwnerVisits),
            })
            .ToListAsync(ct);

        var sources = grouped
            .Where(g => g.PageCategory != TrafficCountryCategory)
            .Select(g => new StoreTrafficSourceDto(g.PageCategory, g.PageFeature, g.Impressions, g.Visits, g.OwnerImpressions, g.OwnerVisits))
            .OrderByDescending(s => s.Visits)
            .ThenByDescending(s => s.Impressions)
            .ToList();

        var countryRows = grouped.Where(g => g.PageCategory == TrafficCountryCategory).ToList();
        var codes = countryRows.Select(c => c.PageFeature).ToList();
        var names = await _db.SteamCountries
            .AsNoTracking()
            .Where(c => codes.Contains(c.CountryCode))
            .ToDictionaryAsync(c => c.CountryCode, ct);
        var countries = countryRows
            .Select(c =>
            {
                names.TryGetValue(c.PageFeature, out var country);
                return new StoreTrafficCountryDto(
                    c.PageFeature, country?.CountryName ?? c.PageFeature, country?.Region ?? string.Empty,
                    c.Impressions, c.Visits);
            })
            .OrderByDescending(c => c.Visits)
            .ThenBy(c => c.CountryCode, StringComparer.Ordinal)
            .ToList();

        return new StoreTrafficBreakdownDto(firstDate, lastDate, firstDailyDate, sources, countries);
    }

    // Nav Stats filter values (see SteamworksNavStat).
    private const string NavStatsAllRegions = "All Regions";
    private const string NavStatsAll = "all";
    private static readonly string[] NavStatsAttributionOrder = ["all", "store", "steamworks", "external"];

    public async Task<NavStatsDto> GetNavStatsAsync(GetNavStatsQuery request, CancellationToken ct = default)
    {
        var publisherId = request.PublisherId ?? await _db.SteamworksNavStats
            .AsNoTracking()
            .OrderByDescending(n => n.PeriodEnd)
            .Select(n => (int?)n.PublisherId)
            .FirstOrDefaultAsync(ct);
        if (publisherId is null)
            return new NavStatsDto(null, null, null, null, [], [], []);

        var all = _db.SteamworksNavStats.AsNoTracking()
            .Where(n => n.PublisherId == publisherId && n.Region == NavStatsAllRegions);
        var firstDate = await all.MinAsync(n => (DateOnly?)n.PeriodStart, ct);
        var lastDate = await all.MaxAsync(n => (DateOnly?)n.PeriodEnd, ct);
        var firstDailyDate = await all.Where(n => n.PeriodStart == n.PeriodEnd).MinAsync(n => (DateOnly?)n.PeriodStart, ct);

        var inRange = all;
        if (request.From is { } f) inRange = inRange.Where(n => n.PeriodStart >= f);
        if (request.To is { } e) inRange = inRange.Where(n => n.PeriodEnd <= e);

        var byApp = await inRange
            .GroupBy(n => new { n.AppId, n.Attribution })
            .Select(g => new { g.Key.AppId, g.Key.Attribution, Impressions = g.Sum(n => n.Impressions), Visits = g.Sum(n => n.Visits) })
            .ToListAsync(ct);

        var periods = (await inRange
            .Where(n => n.Attribution == NavStatsAll)
            .GroupBy(n => new { n.PeriodStart, n.PeriodEnd })
            .Select(g => new NavStatsPeriodDto(g.Key.PeriodStart, g.Key.PeriodEnd, g.Sum(n => n.Impressions), g.Sum(n => n.Visits)))
            .ToListAsync(ct))
            .OrderBy(p => p.PeriodStart)
            .ToList();

        // The name the latest export gave each product.
        var names = (await inRange
            .Where(n => n.Attribution == NavStatsAll && n.AppName != null)
            .Select(n => new { n.AppId, n.AppName, n.PeriodEnd })
            .ToListAsync(ct))
            .GroupBy(n => n.AppId)
            .ToDictionary(g => g.Key, g => g.MaxBy(n => n.PeriodEnd)!.AppName);

        var appIds = byApp.Select(a => a.AppId).Distinct().ToList();
        var gameIds = await _db.Games.AsNoTracking()
            .Where(g => appIds.Contains(g.AppId))
            .ToDictionaryAsync(g => g.AppId, g => g.Id, ct);

        var cells = byApp.ToDictionary(a => (a.AppId, a.Attribution), a => (a.Impressions, a.Visits));
        (long Impressions, long Visits) Cell(int appId, string attribution)
            => cells.TryGetValue((appId, attribution), out var c) ? c : (0, 0);

        var products = appIds
            .Select(id =>
            {
                var a = Cell(id, NavStatsAll);
                var s = Cell(id, "store");
                var p = Cell(id, "steamworks");
                var x = Cell(id, "external");
                return new NavStatsProductDto(
                    id, names.GetValueOrDefault(id), gameIds.TryGetValue(id, out var gameId) ? gameId : null,
                    a.Impressions, a.Visits, s.Impressions, s.Visits, p.Impressions, p.Visits, x.Impressions, x.Visits);
            })
            .OrderByDescending(p => p.Visits)
            .ThenByDescending(p => p.Impressions)
            .ThenBy(p => p.AppId)
            .ToList();

        var totals = NavStatsAttributionOrder
            .Select(attr => new NavStatsTotalDto(
                attr,
                byApp.Where(a => a.Attribution == attr).Sum(a => a.Impressions),
                byApp.Where(a => a.Attribution == attr).Sum(a => a.Visits)))
            .ToList();

        return new NavStatsDto(publisherId, firstDate, lastDate, firstDailyDate, totals, periods, products);
    }

    public async Task<PlayersDto> GetPlayersAsync(GetPlayersQuery request, CancellationToken ct = default)
    {
        var gameId = await _db.Games.AsNoTracking()
            .Where(g => g.AppId == request.AppId)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);
        if (gameId is null)
            return new PlayersDto([], null, [], []);

        var days = (await _db.ActiveUsersHistories.AsNoTracking()
            .Where(a => a.GameId == gameId)
            .Select(a => new { a.RecordedAt, a.Dau, a.PeakConcurrentUsers })
            .ToListAsync(ct))
            .OrderBy(a => a.RecordedAt)
            .Select(a => new PlayerDayDto(DateOnly.FromDateTime(a.RecordedAt), a.Dau, a.PeakConcurrentUsers))
            .ToList();

        var countriesDate = await _db.SteamworksPlayerCountries.AsNoTracking()
            .Where(c => c.GameId == gameId)
            .MaxAsync(c => (DateOnly?)c.SnapshotDate, ct);
        var countries = new List<PlayerCountryDto>();
        if (countriesDate is { } date)
        {
            var rows = await _db.SteamworksPlayerCountries.AsNoTracking()
                .Where(c => c.GameId == gameId && c.SnapshotDate == date)
                .ToListAsync(ct);
            var codes = rows.Select(r => r.CountryCode).ToList();
            var regions = await _db.SteamCountries.AsNoTracking()
                .Where(c => codes.Contains(c.CountryCode))
                .ToDictionaryAsync(c => c.CountryCode, c => c.Region, ct);
            countries = rows
                .Select(r => new PlayerCountryDto(r.CountryCode, r.CountryName, regions.GetValueOrDefault(r.CountryCode) ?? string.Empty, r.SharePercent))
                .OrderByDescending(c => c.SharePercent)
                .ThenBy(c => c.CountryCode, StringComparer.Ordinal)
                .ToList();
        }

        // The latest snapshot of each app (the game and its demo).
        var details = (await _db.SteamworksAppDetails.AsNoTracking()
            .Where(d => d.GameId == gameId)
            .ToListAsync(ct))
            .GroupBy(d => d.AppId)
            .Select(g => g.MaxBy(d => d.SnapshotDate)!)
            .OrderBy(d => d.IsDemo)
            .Select(d => new AppDetailsDto(d.AppId, d.IsDemo, d.SnapshotDate, d.MedianPlaytimeMinutes, d.MedianPlaytimeText, d.LifetimeUniqueUsers))
            .ToList();

        return new PlayersDto(days, countriesDate, countries, details);
    }

    public async Task<List<GroupMemberCountDto>> GetGroupMemberCountAsync(GetGroupMemberCountQuery request, CancellationToken ct = default)
    {
        return await _db.GroupMemberCountSnapshots
            .AsNoTracking()
            .Include(g => g.Game)
            .Where(g => g.Game.AppId == request.AppId)
            .OrderByDescending(g => g.SnapshotDate)
            .Take(request.Limit)
            .Select(g => new GroupMemberCountDto(
                g.Id, g.SnapshotDate, g.MemberCount, g.Error, g.ScrapedAt))
            .ToListAsync(ct);
    }

    public async Task<List<FollowerPointDto>> GetFollowerSeriesAsync(GetFollowerSeriesQuery request, CancellationToken ct = default)
    {
        var gameId = await _db.Games.AsNoTracking()
            .Where(g => g.AppId == request.AppId)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);
        if (gameId is null) return [];

        var members = await _db.GroupMemberCountSnapshots.AsNoTracking()
            .Where(s => s.GameId == gameId && s.Error == null)
            .Select(s => new { s.SnapshotDate, s.MemberCount, s.Source })
            .ToListAsync(ct);
        var listed = await _db.SteamDbSnapshots.AsNoTracking()
            .Where(s => s.GameId == gameId && s.Follows > 0)
            .GroupBy(s => s.SnapshotDate)
            .Select(g => new { Date = g.Key, Follows = g.Max(s => s.Follows) })
            .ToListAsync(ct);

        // The graph is the authority up to its last day. A list value trails the graph
        // by 0-1 days (measured), so it cannot be matched to a graph date: list values
        // and the group scrape only fill the days after the graph ends.
        var graph = members.Where(m => m.Source == GroupMemberCountSnapshot.SteamDbFollowerSource).ToList();
        var graphEnd = graph.Count > 0 ? graph.Max(m => m.SnapshotDate) : DateOnly.MinValue;

        var after = new Dictionary<DateOnly, FollowerPointDto>();
        foreach (var l in listed.Where(l => l.Date > graphEnd))
            after[l.Date] = new FollowerPointDto(l.Date, l.Follows, "steamdb_list");
        foreach (var m in members.Where(m => m.Source == GroupMemberCountSnapshot.SteamCommunitySource && m.SnapshotDate > graphEnd))
            after.TryAdd(m.SnapshotDate, new FollowerPointDto(m.SnapshotDate, m.MemberCount, m.Source));

        return graph
            .Select(m => new FollowerPointDto(m.SnapshotDate, m.MemberCount, m.Source))
            .Concat(after.Values)
            .OrderBy(p => p.Date)
            .ToList();
    }

    public async Task<List<SteamDbSnapshotDto>> GetSteamDbSnapshotsAsync(GetSteamDbSnapshotQuery request, CancellationToken ct = default)
    {
        var query = _db.SteamDbSnapshots
            .AsNoTracking()
            .Include(s => s.Game)
            .Where(s => s.Game.AppId == request.AppId);

        if (!string.IsNullOrWhiteSpace(request.Source))
            query = query.Where(s => s.Source == request.Source);

        return await query
            .OrderByDescending(s => s.SnapshotDate)
            .Take(request.Limit)
            .Select(s => new SteamDbSnapshotDto(
                s.Id, s.SnapshotDate, s.Source, s.Rank,
                s.Price, s.Rating, s.Release,
                s.Follows, s.SevenDayGain, s.ScrapedAt))
            .ToListAsync(ct);
    }

    public async Task<PagedResult<SteamDbRankingDto>> GetSteamDbRankingsAsync(GetSteamDbRankingsQuery request, CancellationToken ct = default)
    {
        var date = request.Date ?? await _db.SteamDbSnapshots
            .AsNoTracking()
            .Where(s => s.Source == request.Source)
            .MaxAsync(s => s.SnapshotDate, ct);

        var query = _db.SteamDbSnapshots
            .AsNoTracking()
            .Include(s => s.Game)
            .Where(s => s.Source == request.Source && s.SnapshotDate == date);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(s => EF.Functions.ILike(s.Game.Name, $"%{request.Search}%"));

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(s => s.Rank)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new SteamDbRankingDto(
                s.Id, s.Game.AppId, s.GameId, s.Game.Name,
                s.Game.HeaderImageUrl, s.SnapshotDate, s.Source,
                s.Rank, s.Price, s.Rating, s.Release,
                s.Follows, s.SevenDayGain, s.ScrapedAt))
            .ToListAsync(ct);

        return new PagedResult<SteamDbRankingDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public async Task<List<DateOnly>> GetAvailableDatesAsync(GetAvailableDatesQuery request, CancellationToken ct = default)
    {
        return await _db.SteamDbSnapshots
            .AsNoTracking()
            .Where(s => s.Source == request.Source)
            .Select(s => s.SnapshotDate)
            .Distinct()
            .OrderByDescending(d => d)
            .Take(90)
            .ToListAsync(ct);
    }
}
