using Carter;
using Cato.API.Features._Common;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Games;

public static class DeleteGame
{
    public record Command(Guid Id) : IRequest<Result<bool>>;

    public class Handler : IRequestHandler<Command, Result<bool>>
    {
        private readonly CatoDbContext _db;

        public Handler(CatoDbContext db) => _db = db;

        public async Task<Result<bool>> Handle(Command request, CancellationToken ct)
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == request.Id, ct);
            if (game is null)
                return Result<bool>.Failure($"Game with Id {request.Id} not found.");

            _db.Games.Remove(game); // Cascade deletes genres & tags
            await _db.SaveChangesAsync(ct);

            return Result<bool>.Success(true);
        }
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapDelete("/api/games/{id:guid}", async (Guid id, IMediator mediator) =>
            {
                var result = await mediator.Send(new Command(id));
                return result.IsSuccess
                    ? Results.NoContent()
                    : Results.NotFound(result.ErrorMessage);
            })
            .WithName("DeleteGame")
            .WithTags("Games")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
        }
    }
}
