using Carter;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Ingestion;

public static class GetTrafficData
{
    public record Query(int AppId, string? Source, int Limit = 100) : IRequest<List<TrafficDto>>;

    public record TrafficDto(
        Guid Id,
        DateOnly TrafficDate,
        int WishlistAdditions,
        int WishlistDeletions,
        int NetWishlistChange,
        int Purchases,
        string? TrafficSource,
        decimal? PurchaseConversionRate);

    public class Handler : IRequestHandler<Query, List<TrafficDto>>
    {
        private readonly CatoDbContext _db;
        public Handler(CatoDbContext db) => _db = db;

        public async Task<List<TrafficDto>> Handle(Query request, CancellationToken ct)
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

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/games/{appId}/traffic", async (int appId, string? source, int? limit, IMediator mediator) =>
            {
                var result = await mediator.Send(new Query(appId, source, limit ?? 100));
                return Results.Ok(result);
            })
            .WithTags("Game Data")
            .WithDescription("Get wishlist/traffic data for a game");
        }
    }
}
