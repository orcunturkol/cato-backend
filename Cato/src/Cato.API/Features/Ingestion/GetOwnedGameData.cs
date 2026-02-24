using Carter;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Ingestion;

public static class GetOwnedGameData
{
    public record Query(int AppId, int Limit = 30) : IRequest<List<OwnedGameDto>>;

    public record OwnedGameDto(
        Guid Id,
        DateOnly SnapshotDate,
        int WishlistAdditions,
        int WishlistDeletions,
        int PurchasesAndActivations,
        int Gifts,
        int PeriodWishlistBalance);

    public class Handler : IRequestHandler<Query, List<OwnedGameDto>>
    {
        private readonly CatoDbContext _db;
        public Handler(CatoDbContext db) => _db = db;

        public async Task<List<OwnedGameDto>> Handle(Query request, CancellationToken ct)
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

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/games/{appId}/owned-data", async (int appId, int? limit, IMediator mediator) =>
            {
                var result = await mediator.Send(new Query(appId, limit ?? 30));
                return Results.Ok(result);
            })
            .WithTags("Game Data")
            .WithDescription("Get owned game wishlist/activation snapshots");
        }
    }
}
