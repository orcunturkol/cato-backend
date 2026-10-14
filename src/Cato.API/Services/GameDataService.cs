using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Models.SteamDb;
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
                GrossUsd = g.Sum(s => s.GrossRevenueUsd),
                ReturnsUsd = g.Sum(s => s.GrossReturnsUsd),
                NetUsd = g.Sum(s => s.NetRevenueUsd),
            })
            .ToListAsync(ct);

        return days
            .OrderBy(d => d.Date)
            .Select(d => new SalesDailyDto(d.Date, d.Gross, d.Returned, d.Gross - d.Returned, d.GrossUsd, d.ReturnsUsd, d.NetUsd))
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
                    t.Gross, t.Returned, t.Gross - t.Returned,
                    t.GrossUsd, t.NetUsd, t.NetUnits30d, t.NetUsd30d);
            })
            .OrderByDescending(r => r.NetUsd)
            .ToList();

        return new SalesSummaryDto(latest, rows);
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
