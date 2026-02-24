using Cato.API.Models.Ingestion;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetOwnedGameDataHandler : IRequestHandler<GetOwnedGameDataQuery, List<OwnedGameDto>>
{
    private readonly CatoDbContext _db;
    public GetOwnedGameDataHandler(CatoDbContext db) => _db = db;

    public async Task<List<OwnedGameDto>> Handle(GetOwnedGameDataQuery request, CancellationToken ct)
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
