using Cato.API.Models.Ingestion;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetTrafficDataHandler : IRequestHandler<GetTrafficDataQuery, List<TrafficDto>>
{
    private readonly CatoDbContext _db;
    public GetTrafficDataHandler(CatoDbContext db) => _db = db;

    public async Task<List<TrafficDto>> Handle(GetTrafficDataQuery request, CancellationToken ct)
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
}
