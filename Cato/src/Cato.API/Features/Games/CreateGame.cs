using Carter;
using Cato.API.Features._Common;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Games;

public static class CreateGame
{
    public record Command(
        int AppId,
        string? Name,
        string? GameType,
        string? DeveloperName,
        string? PublisherName
    ) : IRequest<Result<GameDto>>;

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.AppId).GreaterThan(0).WithMessage("AppId must be a positive integer.");
        }
    }

    public class Handler : IRequestHandler<Command, Result<GameDto>>
    {
        private readonly CatoDbContext _db;

        public Handler(CatoDbContext db) => _db = db;

        public async Task<Result<GameDto>> Handle(Command request, CancellationToken ct)
        {
            // Check uniqueness
            var exists = await _db.Games.AnyAsync(g => g.AppId == request.AppId, ct);
            if (exists)
                return Result<GameDto>.Failure($"A game with AppId {request.AppId} already exists.");

            // Upsert developer
            LegalEntity? developer = null;
            if (!string.IsNullOrWhiteSpace(request.DeveloperName))
            {
                developer = await _db.LegalEntities
                    .FirstOrDefaultAsync(e => e.Name == request.DeveloperName && e.EntityType == "Developer", ct);
                if (developer is null)
                {
                    developer = new LegalEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = request.DeveloperName,
                        EntityType = "Developer"
                    };
                    _db.LegalEntities.Add(developer);
                }
            }

            // Upsert publisher
            LegalEntity? publisher = null;
            if (!string.IsNullOrWhiteSpace(request.PublisherName))
            {
                publisher = await _db.LegalEntities
                    .FirstOrDefaultAsync(e => e.Name == request.PublisherName && e.EntityType == "Publisher", ct);
                if (publisher is null)
                {
                    publisher = new LegalEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = request.PublisherName,
                        EntityType = "Publisher"
                    };
                    _db.LegalEntities.Add(publisher);
                }
            }

            var game = new Game
            {
                Id = Guid.NewGuid(),
                AppId = request.AppId,
                Name = request.Name ?? $"App {request.AppId}",
                GameType = request.GameType ?? "Owned",
                DeveloperId = developer?.Id,
                PublisherId = publisher?.Id
            };

            _db.Games.Add(game);
            await _db.SaveChangesAsync(ct);

            // Reload with navigation properties
            game = await _db.Games
                .Include(g => g.Developer)
                .Include(g => g.Publisher)
                .Include(g => g.Genres)
                .Include(g => g.Tags)
                .FirstAsync(g => g.Id == game.Id, ct);

            return Result<GameDto>.Success(game.ToDto());
        }
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/games", async (Command command, IMediator mediator, IValidator<Command> validator) =>
            {
                var validation = await validator.ValidateAsync(command);
                if (!validation.IsValid)
                    return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

                var result = await mediator.Send(command);
                return result.IsSuccess
                    ? Results.Created($"/api/games/{result.Data!.Id}", result.Data)
                    : Results.BadRequest(result.ErrorMessage);
            })
            .WithName("CreateGame")
            .WithTags("Games")
            .Produces<GameDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);
        }
    }
}
