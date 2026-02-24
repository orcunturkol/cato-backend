using Carter;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Ingestion;

public static class GetCcuHistory
{
    public record Query(int AppId, string? Source, int Limit = 100) : IRequest<List<CcuDto>>;

    public record CcuDto(
        Guid Id,
        DateTime Timestamp,
        int CcuCount,
        string? Source);

    public class Handler : IRequestHandler<Query, List<CcuDto>>
    {
        private readonly CatoDbContext _db;
        public Handler(CatoDbContext db) => _db = db;

        public async Task<List<CcuDto>> Handle(Query request, CancellationToken ct)
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
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/games/{appId}/ccu", async (int appId, string? source, int? limit, IMediator mediator) =>
            {
                var result = await mediator.Send(new Query(appId, source, limit ?? 100));
                return Results.Ok(result);
            })
            .WithTags("Game Data")
            .WithDescription("Get peak CCU history for a game");
        }
    }
}
