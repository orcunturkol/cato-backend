using System.Text.Json;
using Carter;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Ingestion;

public static class IngestOwnedGameData
{
    public record Command(int AppId, string FilePath) : IRequest<IngestionResult>;

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.AppId).GreaterThan(0);
            RuleFor(x => x.FilePath).NotEmpty();
        }
    }

    public class Handler : IRequestHandler<Command, IngestionResult>
    {
        private readonly CatoDbContext _db;
        private readonly ILogger<Handler> _logger;

        public Handler(CatoDbContext db, ILogger<Handler> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<IngestionResult> Handle(Command request, CancellationToken ct)
        {
            var log = new IngestionLog
            {
                Id = Guid.NewGuid(),
                Source = "steamworks_owned_game",
                StartTime = DateTime.UtcNow,
                Status = "Running",
                FilePath = request.FilePath
            };
            _db.IngestionLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            try
            {
                var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
                if (game is null)
                    throw new InvalidOperationException($"Game with AppId {request.AppId} not found. Create the game first.");

                if (!File.Exists(request.FilePath))
                    throw new FileNotFoundException($"File not found: {request.FilePath}");

                var json = await File.ReadAllTextAsync(request.FilePath, ct);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                int processed = 1, inserted = 0, failed = 0;

                // wishlist_activity.json: {
                //   "game_id": "2429270",
                //   "game_name": "The RPG",
                //   "Wishlist Additions": "135,427",
                //   "Wishlist Deletions": "22,490",
                //   "Wishlist Purchases and Activations": "11,703",
                //   "Wishlist Gifts": "433",
                //   "Period Wishlist Balance": "100,801"
                // }
                try
                {
                    var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);

                    var additions = ParseFormattedInt(root.GetProperty("Wishlist Additions").GetString());
                    var deletions = ParseFormattedInt(root.GetProperty("Wishlist Deletions").GetString());
                    var purchases = ParseFormattedInt(root.GetProperty("Wishlist Purchases and Activations").GetString());
                    var gifts = ParseFormattedInt(root.GetProperty("Wishlist Gifts").GetString());
                    var balance = ParseFormattedInt(root.GetProperty("Period Wishlist Balance").GetString());

                    // Check for existing record for today (idempotency)
                    var exists = await _db.OwnedGameData.AnyAsync(
                        o => o.GameId == game.Id && o.SnapshotDate == snapshotDate, ct);

                    if (!exists)
                    {
                        _db.OwnedGameData.Add(new OwnedGameData
                        {
                            Id = Guid.NewGuid(),
                            GameId = game.Id,
                            SnapshotDate = snapshotDate,
                            WishlistAdditions = additions,
                            WishlistDeletions = deletions,
                            PurchasesAndActivations = purchases,
                            Gifts = gifts,
                            PeriodWishlistBalance = balance
                        });
                        inserted = 1;
                    }
                }
                catch (Exception ex)
                {
                    failed = 1;
                    _logger.LogWarning(ex, "Failed to process owned game data");
                }

                await _db.SaveChangesAsync(ct);

                log.EndTime = DateTime.UtcNow;
                log.Status = "Completed";
                log.RecordsProcessed = processed;
                log.RecordsInserted = inserted;
                log.RecordsFailed = failed;
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Owned game data ingestion complete: {Inserted} inserted", inserted);

                return new IngestionResult(processed, inserted, 0, failed, log.Id);
            }
            catch (Exception ex)
            {
                log.EndTime = DateTime.UtcNow;
                log.Status = "Failed";
                log.ErrorMessage = ex.Message;
                await _db.SaveChangesAsync(ct);
                throw;
            }
        }

        /// <summary>Parses numbers formatted with commas like "135,427" → 135427</summary>
        private static int ParseFormattedInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            return int.Parse(value.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/ingestion/owned-game", async (Command command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);
                return Results.Ok(result);
            })
            .WithTags("Ingestion")
            .WithDescription("Ingest owned game wishlist/activation data from JSON");
        }
    }
}
