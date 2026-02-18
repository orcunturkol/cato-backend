using Carter;
using Cato.API.Features._Common;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Games;

public static class GetGameDetails
{
    public record Query(Guid Id) : IRequest<Result<GameDto>>;

    public class Handler : IRequestHandler<Query, Result<GameDto>>
    {
        private readonly CatoDbContext _db;

        public Handler(CatoDbContext db) => _db = db;

        public async Task<Result<GameDto>> Handle(Query request, CancellationToken ct)
        {
            var game = await _db.Games
                .AsNoTracking()
                .Include(g => g.Developer)
                .Include(g => g.Publisher)
                .Include(g => g.Genres)
                .Include(g => g.Tags)
                .FirstOrDefaultAsync(g => g.Id == request.Id, ct);

            return game is null
                ? Result<GameDto>.Failure($"Game with Id {request.Id} not found.")
                : Result<GameDto>.Success(game.ToDto());
        }
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/games/{id:guid}", async (Guid id, IMediator mediator) =>
            {
                var result = await mediator.Send(new Query(id));
                return result.IsSuccess
                    ? Results.Ok(result.Data)
                    : Results.NotFound(result.ErrorMessage);
            })
            .WithName("GetGameDetails")
            .WithTags("Games")
            .Produces<GameDto>()
            .Produces(StatusCodes.Status404NotFound);
        }
    }
}
