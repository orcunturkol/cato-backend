using Cato.API.Models.Ingestion;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetFinancialDataHandler : IRequestHandler<GetFinancialDataQuery, List<FinancialDto>>
{
    private readonly CatoDbContext _db;
    public GetFinancialDataHandler(CatoDbContext db) => _db = db;

    public async Task<List<FinancialDto>> Handle(GetFinancialDataQuery request, CancellationToken ct)
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
}
