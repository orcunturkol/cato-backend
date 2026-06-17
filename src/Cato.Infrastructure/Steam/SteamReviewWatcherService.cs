using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam.Models;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

public class SteamReviewWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SteamReviewWatcherService> _logger;
    private readonly SteamSettings _settings;

    public SteamReviewWatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<SteamReviewWatcherService> logger,
        IOptions<SteamSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay to let the database finish migrating.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunReviewSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Steam review sync cycle failed");
            }

            await Task.Delay(
                TimeSpan.FromHours(_settings.ReviewCheckIntervalHours),
                stoppingToken);
        }
    }

    private async Task RunReviewSyncAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var steamApi = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
        var rotation = scope.ServiceProvider.GetRequiredService<ISteamIdRotationService>();

        var games = await db.Games
            .Where(g => g.GameType == "Sourcing" || g.GameType == "Owned")
            .AsNoTracking()
            .Select(g => new { g.Id, g.AppId, g.Name })
            .ToListAsync(ct);

        _logger.LogInformation(
            "Steam review sync starting — {Count} Sourcing/Owned games", games.Count);

        foreach (var game in games)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await SyncGameReviewsAsync(db, steamApi, rotation, game.Id, game.AppId, game.Name, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to sync reviews for AppId {AppId} ({Name})", game.AppId, game.Name);
            }
        }

        _logger.LogInformation("Steam review sync cycle complete");
    }

    private async Task SyncGameReviewsAsync(
        CatoDbContext db,
        ISteamApiService steamApi,
        ISteamIdRotationService rotation,
        Guid gameId, int appId, string gameName,
        CancellationToken ct)
    {
        // Watermark: newest review we already have stored for this game.
        var watermark = await db.SteamReviews
            .Where(r => r.GameId == gameId)
            .MaxAsync(r => (DateTime?)r.TimestampCreated, ct);

        // Pre-load known RecommendationIds newer than the watermark to avoid
        // re-inserting reviews returned twice by Steam's cursor overlap.
        var knownIds = await db.SteamReviews
            .Where(r => r.GameId == gameId &&
                        (!watermark.HasValue || r.TimestampCreated > watermark.Value))
            .Select(r => r.RecommendationId)
            .ToHashSetAsync(ct);

        var cursor = "*";
        var isFirstPage = true;
        var totalInserted = 0;
        var maxPerGame = _settings.ReviewBackfillMaxPerGame;

        while (totalInserted < maxPerGame && !ct.IsCancellationRequested)
        {
            var response = await steamApi.GetAppReviewsAsync(appId, cursor, ct);

            if (response is null || response.Reviews is null || response.Reviews.Count == 0)
                break;

            // Upsert today's summary snapshot from the first page's query_summary.
            if (isFirstPage)
            {
                await InsertSummarySnapshotAsync(db, gameId, response.QuerySummary, ct);
                isFirstPage = false;
            }

            var batch = new List<SteamReview>();
            var pageSteamIds = new HashSet<long>();
            var hitWatermark = false;

            foreach (var item in response.Reviews)
            {
                if (item.RecommendationId is null) continue;

                var created = DateTimeOffset.FromUnixTimeSeconds(item.TimestampCreated).UtcDateTime;

                // Stop paginating once we reach reviews we already have.
                if (watermark.HasValue && created <= watermark.Value)
                {
                    hitWatermark = true;
                    break;
                }

                if (knownIds.Contains(item.RecommendationId))
                    continue;

                knownIds.Add(item.RecommendationId);

                long? authorSteamId = null;
                if (long.TryParse(item.Author?.SteamId, out var sid))
                {
                    authorSteamId = sid;
                    pageSteamIds.Add(sid);
                }

                batch.Add(new SteamReview
                {
                    Id = Guid.NewGuid(),
                    GameId = gameId,
                    RecommendationId = item.RecommendationId,
                    AuthorSteamId = authorSteamId,
                    VotedUp = item.VotedUp,
                    Language = item.Language ?? string.Empty,
                    ReviewText = item.ReviewText ?? string.Empty,
                    PlaytimeForeverMinutes = item.Author?.PlaytimeForever ?? 0,
                    PlaytimeAtReviewMinutes = item.Author?.PlaytimeAtReview ?? 0,
                    VotesUp = item.VotesUp,
                    VotesFunny = item.VotesFunny,
                    SteamPurchase = item.SteamPurchase,
                    ReceivedForFree = item.ReceivedForFree,
                    WrittenDuringEarlyAccess = item.WrittenDuringEarlyAccess,
                    TimestampCreated = created,
                    TimestampUpdated = DateTimeOffset.FromUnixTimeSeconds(item.TimestampUpdated).UtcDateTime,
                });
            }

            if (batch.Count > 0)
            {
                db.SteamReviews.AddRange(batch);
                await db.SaveChangesAsync(ct);
                totalInserted += batch.Count;

                // Seed only after the reviews are persisted so the rotation
                // never references an id we failed to store. Best-effort.
                await rotation.SeedAsync(pageSteamIds, ct);
            }

            // Steam signals end-of-results by returning the same cursor or "*".
            if (hitWatermark || response.Cursor == cursor || response.Cursor is null or "*")
                break;

            cursor = response.Cursor;
        }

        // Refresh Game.SteamReviewScore and Game.ReviewCount from the latest snapshot.
        await UpdateGameReviewFieldsAsync(db, gameId, ct);

        _logger.LogInformation(
            "Synced {Total} new reviews for AppId {AppId} ({Name})",
            totalInserted, appId, gameName);
    }

    private async Task InsertSummarySnapshotAsync(
        CatoDbContext db, Guid gameId, SteamReviewQuerySummary? summary, CancellationToken ct)
    {
        if (summary is null) return;

        db.ReviewSummarySnapshots.Add(new ReviewSummarySnapshot
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            SnapshotDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReviewScore = summary.ReviewScore,
            ReviewScoreDesc = summary.ReviewScoreDesc ?? string.Empty,
            TotalPositive = summary.TotalPositive,
            TotalNegative = summary.TotalNegative,
            TotalReviews = summary.TotalReviews,
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateGameReviewFieldsAsync(
        CatoDbContext db, Guid gameId, CancellationToken ct)
    {
        var latest = await db.ReviewSummarySnapshots
            .Where(s => s.GameId == gameId)
            .OrderByDescending(s => s.SnapshotDate)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latest is null) return;

        var game = await db.Games.FindAsync([gameId], ct);
        if (game is null) return;

        game.SteamReviewScore = latest.ReviewScoreDesc;
        game.ReviewCount = latest.TotalReviews;
        await db.SaveChangesAsync(ct);
    }
}
