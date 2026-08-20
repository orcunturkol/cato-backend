using System.Text.Json;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Messaging;
using Cato.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;

namespace Cato.API.Services;

/// <summary>
/// Handles one <c>game.analyzed</c> event: makes sure the game exists in CATO,
/// stamps <c>AnalyzedAt</c>, then moves it to the front of the follower-history
/// queue.
///
/// Everything it does is database and Redis work, deliberately. This consumer is
/// serial with a prefetch of one, so any slow call here stalls the whole queue —
/// an earlier version enriched from the Steam store inline and a 1310-message
/// backfill dropped the queue to six messages per ten minutes, because Steam
/// calls share one process-wide throttle with the enrichment watcher. Enrichment
/// belongs to <c>GameEnrichmentWatcherService</c>; <c>AnalyzedAt</c> is what puts
/// this game at the front of its queue.
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

    private readonly CatoDbContext _db;
    private readonly IRedisAppIdSyncService _redisSync;
    private readonly ILogger<GameAnalyzedDispatcher> _logger;

    public GameAnalyzedDispatcher(
        CatoDbContext db,
        IRedisAppIdSyncService redisSync,
        ILogger<GameAnalyzedDispatcher> logger)
    {
        _db = db;
        _redisSync = redisSync;
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

            // Redis last, and outside any transaction — see the class remarks.
            await _redisSync.PrioritizeFollowerHistoryAsync(msg.AppId, msg.ExtractedAt, ct);

            _logger.LogInformation("game_analyzed_processed gameId={GameId}", game.Id);
        }
    }
}
