using Cato.API.Models.Ingestion;
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
}
