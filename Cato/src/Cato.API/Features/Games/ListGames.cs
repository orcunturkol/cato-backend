using Carter;
using Cato.API.Features._Common;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Games;

public static class ListGames
{
    public record Query(
        string? GameType = null,
        string? Search = null,
        int Page = 1,
        int PageSize = 20
    ) : IRequest<PagedResult<GameDto>>;

    public class Handler : IRequestHandler<Query, PagedResult<GameDto>>
    {
        private readonly CatoDbContext _db;

        public Handler(CatoDbContext db) => _db = db;

        public async Task<PagedResult<GameDto>> Handle(Query request, CancellationToken ct)
        {
            var query = _db.Games
                .AsNoTracking()
                .Include(g => g.Developer)
                .Include(g => g.Publisher)
                .Include(g => g.Genres)
                .Include(g => g.Tags)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.GameType))
                query = query.Where(g => g.GameType == request.GameType);

            if (!string.IsNullOrWhiteSpace(request.Search))
                query = query.Where(g =>
                    EF.Functions.ILike(g.Name, $"%{request.Search}%") ||
                    g.Genres.Any(genre => EF.Functions.ILike(genre.GenreName, $"%{request.Search}%")) ||
                    g.Tags.Any(tag => EF.Functions.ILike(tag.TagName, $"%{request.Search}%")));

            var totalCount = await query.CountAsync(ct);

            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);

            var items = await query
                .OrderBy(g => g.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PagedResult<GameDto>
            {
                Items = items.Select(g => g.ToDto()).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/games", async (
                string? gameType,
                string? search,
                int? page,
                int? pageSize,
                IMediator mediator) =>
            {
                var result = await mediator.Send(new Query(gameType, search, page ?? 1, pageSize ?? 20));
                return Results.Ok(result);
            })
            .WithName("ListGames")
            .WithTags("Games")
            .Produces<PagedResult<GameDto>>();
        }
    }
}
