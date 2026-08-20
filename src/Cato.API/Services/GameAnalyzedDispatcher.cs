using System.Text.Json;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Messaging;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace Cato.API.Services;

/// <summary>
/// Handles one <c>game.analyzed</c> event: makes sure the game exists in CATO,
/// enriches it from the Steam store if it never has been, then moves it to the
/// front of the follower-history queue.
///
/// Deliberately not routed through <see cref="BatchIngestionDispatcher"/>. That
/// path runs every item inside one database transaction, and a Redis write there
/// would not roll back with it. Here the Redis write happens after
/// SaveChangesAsync and outside any transaction — matching how
/// <c>GameService.CreateAsync</c> and <c>SteamGameEnrichmentService</c> sync Redis.
/// </summary>
public class GameAnalyzedDispatcher : IGameAnalyzedDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const int SupportedSchemaVersion = 1;

    /// <summary>
    /// Consecutive enrichment failures after which the event path stops trying inline.
    /// An app with no store page answers the same way every time; the watcher owns
    /// the longer retry budget.
    /// </summary>
    private const int MaxInlineEnrichmentFailures = 3;

    private readonly CatoDbContext _db;
    private readonly IRedisAppIdSyncService _redisSync;
    private readonly ISteamGameEnrichmentService _enrichment;
    private readonly ILogger<GameAnalyzedDispatcher> _logger;

    public GameAnalyzedDispatcher(
        CatoDbContext db,
        IRedisAppIdSyncService redisSync,
        ISteamGameEnrichmentService enrichment,
        ILogger<GameAnalyzedDispatcher> logger)
    {
        _db = db;
        _redisSync = redisSync;
        _enrichment = enrichment;
        _logger = logger;
    }

    public async Task DispatchAsync(string messageJson, CancellationToken ct)
    {
        var msg = JsonSerializer.Deserialize<GameAnalyzedMessage>(messageJson, JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize game.analyzed message");

        if (msg.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidOperationException(
                $"Unsupported game.analyzed schema_version={msg.SchemaVersion} (expected {SupportedSchemaVersion})");

        if (msg.AppId <= 0)
            throw new InvalidOperationException($"game.analyzed message has invalid app_id={msg.AppId}");

        using (LogContext.PushProperty("AppId", msg.AppId))
        using (LogContext.PushProperty("RedditId", msg.RedditId ?? "-"))
        {
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == msg.AppId, ct);

            if (game is null)
            {
                // reddit_metrics routinely surfaces games CATO has never seen —
                // small indie titles that never showed up in a SteamDB ranking.
                // Creating the row here is the point: it is what "enrich the game
                // data" means for a game we would otherwise have no record of.
                game = new Game
                {
                    Id = Guid.NewGuid(),
                    AppId = msg.AppId,
                    Name = !string.IsNullOrWhiteSpace(msg.GameName) ? msg.GameName! : $"App {msg.AppId}",
                    GameType = "Other",
                    AnalyzedAt = msg.ExtractedAt.UtcDateTime
                };
                _db.Games.Add(game);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("game_analyzed_stub_created name='{Name}'", game.Name);
            }
            else
            {
                game.AnalyzedAt = msg.ExtractedAt.UtcDateTime;

                // Replace a placeholder name with the real one; never overwrite a
                // name that came from Steam enrichment with reddit's version.
                if (!string.IsNullOrWhiteSpace(msg.GameName)
                    && game.Name.StartsWith("App ", StringComparison.Ordinal))
                    game.Name = msg.GameName!;

                await _db.SaveChangesAsync(ct);
            }

            await EnrichIfNeverEnrichedAsync(game, ct);

            // Redis last, and outside any transaction — see the class remarks.
            await _redisSync.PrioritizeFollowerHistoryAsync(msg.AppId, msg.ExtractedAt, ct);

            _logger.LogInformation("game_analyzed_processed gameId={GameId}", game.Id);
        }
    }

    /// <summary>
    /// Pulls genres, tags, release date and the rest of the store record for a game
    /// that has never been enriched — the reason a reddit-discovered game is worth
    /// having a row for at all.
    ///
    /// Best-effort by design: enrichment is two throttled HTTP calls to Steam, and a
    /// Steam outage must not nack a message whose real work (the row, the Redis
    /// priority) already succeeded. Anything missed here is swept up later by
    /// <c>GameEnrichmentWatcherService</c>.
    ///
    /// Note the deliberate omission of the post-enrichment quality filter that
    /// <c>SteamPicsWatcherService</c> runs: that filter <em>deletes</em> the row, which
    /// would drop a game reddit_metrics holds extractions and follower history for.
    /// </summary>
    private async Task EnrichIfNeverEnrichedAsync(Game game, CancellationToken ct)
    {
        if (game.LastEnrichedAt is not null) return;
        if (game.EnrichmentFailures >= MaxInlineEnrichmentFailures)
        {
            _logger.LogDebug("game_analyzed_enrichment_skipped failures={Failures}", game.EnrichmentFailures);
            return;
        }

        try
        {
            var enriched = await _enrichment.EnrichGameAsync(game.Id, ct);
            _logger.LogInformation("game_analyzed_enriched success={Success}", enriched);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "game_analyzed_enrichment_failed gameId={GameId}", game.Id);
        }
    }
}
