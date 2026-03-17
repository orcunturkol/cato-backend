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
