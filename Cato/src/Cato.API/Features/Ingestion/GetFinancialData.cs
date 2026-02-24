using Carter;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Ingestion;

public static class GetFinancialData
{
    public record Query(int AppId, string? CountryCode, int Limit = 100) : IRequest<List<FinancialDto>>;

    public record FinancialDto(
        Guid Id,
        DateOnly SaleDate,
        string? CountryCode,
        int SalesUnits,
        int ReturnsUnits,
        int NetUnits,
        decimal GrossRevenueUsd,
        decimal NetRevenueUsd,
        string? SaleType,
        string? Platform);

    public class Handler : IRequestHandler<Query, List<FinancialDto>>
    {
        private readonly CatoDbContext _db;
        public Handler(CatoDbContext db) => _db = db;

        public async Task<List<FinancialDto>> Handle(Query request, CancellationToken ct)
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

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/games/{appId}/financials", async (int appId, string? countryCode, int? limit, IMediator mediator) =>
            {
                var result = await mediator.Send(new Query(appId, countryCode, limit ?? 100));
                return Results.Ok(result);
            })
            .WithTags("Game Data")
            .WithDescription("Get financial/sales data for a game");
        }
    }
}
