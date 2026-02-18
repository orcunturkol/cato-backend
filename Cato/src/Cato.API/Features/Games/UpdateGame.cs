using Carter;
using Cato.API.Features._Common;
using Cato.Infrastructure.Database;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Games;

public static class UpdateGame
{
    public record Command(
        Guid Id,
        string? Name,
        string? GameType,
        decimal? PriceUsd,
        bool? IsEarlyAccess,
        bool? IsReleased,
        string? ShortDescription,
        string? Website,
        string? SteamReviewScore,
        int? ReviewCount,
        int? FollowersCount
    ) : IRequest<Result<GameDto>>;

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Id).NotEmpty();
            When(x => x.PriceUsd.HasValue, () =>
                RuleFor(x => x.PriceUsd!.Value).GreaterThanOrEqualTo(0));
        }
    }

    public class Handler : IRequestHandler<Command, Result<GameDto>>
    {
        private readonly CatoDbContext _db;

        public Handler(CatoDbContext db) => _db = db;

        public async Task<Result<GameDto>> Handle(Command request, CancellationToken ct)
        {
            var game = await _db.Games
                .Include(g => g.Developer)
                .Include(g => g.Publisher)
                .Include(g => g.Genres)
                .Include(g => g.Tags)
                .FirstOrDefaultAsync(g => g.Id == request.Id, ct);

            if (game is null)
                return Result<GameDto>.Failure($"Game with Id {request.Id} not found.");

            // Partial update — only set fields that were provided
            if (request.Name is not null) game.Name = request.Name;
            if (request.GameType is not null) game.GameType = request.GameType;
            if (request.PriceUsd.HasValue) game.PriceUsd = request.PriceUsd.Value;
            if (request.IsEarlyAccess.HasValue) game.IsEarlyAccess = request.IsEarlyAccess.Value;
            if (request.IsReleased.HasValue) game.IsReleased = request.IsReleased.Value;
            if (request.ShortDescription is not null) game.ShortDescription = request.ShortDescription;
            if (request.Website is not null) game.Website = request.Website;
            if (request.SteamReviewScore is not null) game.SteamReviewScore = request.SteamReviewScore;
            if (request.ReviewCount.HasValue) game.ReviewCount = request.ReviewCount.Value;
            if (request.FollowersCount.HasValue) game.FollowersCount = request.FollowersCount.Value;

            await _db.SaveChangesAsync(ct);

            return Result<GameDto>.Success(game.ToDto());
        }
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapPatch("/api/games/{id:guid}", async (Guid id, Command command, IMediator mediator, IValidator<Command> validator) =>
            {
                // Ensure route id matches body id
                var cmd = command with { Id = id };
                var validation = await validator.ValidateAsync(cmd);
                if (!validation.IsValid)
                    return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

                var result = await mediator.Send(cmd);
                return result.IsSuccess
                    ? Results.Ok(result.Data)
                    : Results.NotFound(result.ErrorMessage);
            })
            .WithName("UpdateGame")
            .WithTags("Games")
            .Produces<GameDto>()
            .Produces(StatusCodes.Status404NotFound);
        }
    }
}
