using System.Globalization;
using Carter;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Ingestion;

public static class IngestWishlistData
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
                Source = "steamworks_wishlist",
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

                var lines = await File.ReadAllLinesAsync(request.FilePath, ct);

                int processed = 0, inserted = 0, failed = 0;

                // CSV format: first 3 lines are headers
                // Line 0: "Steam Wishlists for <game> (<appid>)"
                // Line 1: blank
                // Line 2: "DateLocal,Game,Adds,Deletes,PurchasesAndActivations,Gifts"
                // Line 3+: data rows
                var dataStart = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("DateLocal,", StringComparison.OrdinalIgnoreCase))
                    {
                        dataStart = i + 1;
                        break;
                    }
                }

                for (int i = dataStart; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    processed++;
                    try
                    {
                        var parts = line.Split(',');
                        if (parts.Length < 6) continue;

                        var trafficDate = DateOnly.Parse(parts[0], CultureInfo.InvariantCulture);
                        var adds = int.Parse(parts[2], CultureInfo.InvariantCulture);
                        var deletes = int.Parse(parts[3], CultureInfo.InvariantCulture);
                        var purchases = int.Parse(parts[4], CultureInfo.InvariantCulture);
                        var gifts = int.Parse(parts[5], CultureInfo.InvariantCulture);

                        // Check for existing record (idempotency)
                        var exists = await _db.SteamTraffic.AnyAsync(
                            t => t.GameId == game.Id &&
                                 t.TrafficDate == trafficDate &&
                                 t.TrafficSource == "steamworks_wishlist",
                            ct);

                        if (!exists)
                        {
                            _db.SteamTraffic.Add(new SteamTraffic
                            {
                                Id = Guid.NewGuid(),
                                GameId = game.Id,
                                TrafficDate = trafficDate,
                                WishlistAdditions = adds,
                                WishlistDeletions = deletes,
                                Purchases = purchases,
                                TrafficSource = "steamworks_wishlist"
                            });
                            inserted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogWarning(ex, "Failed to parse wishlist CSV line {Line}", i);
                    }
                }

                await _db.SaveChangesAsync(ct);

                log.EndTime = DateTime.UtcNow;
                log.Status = "Completed";
                log.RecordsProcessed = processed;
                log.RecordsInserted = inserted;
                log.RecordsFailed = failed;
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Wishlist ingestion complete: {Processed} processed, {Inserted} inserted, {Failed} failed",
                    processed, inserted, failed);

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
    }

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapPost("/api/ingestion/wishlists", async (Command command, IMediator mediator) =>
            {
                var result = await mediator.Send(command);
                return Results.Ok(result);
            })
            .WithTags("Ingestion")
            .WithDescription("Ingest Steamworks wishlist CSV data");
        }
    }
}
