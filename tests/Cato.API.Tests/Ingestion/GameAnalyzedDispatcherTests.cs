using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers <see cref="GameAnalyzedDispatcher"/>. The contract worth protecting is the
/// handoff: this consumer does database and Redis work only, and hands enrichment to
/// the watcher by stamping <c>AnalyzedAt</c>. An earlier version called Steam inline
/// and starved the queue, so "the game ends up at the front of the enrichment queue"
/// is asserted here directly rather than assumed.
/// </summary>
public class GameAnalyzedDispatcherTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public GameAnalyzedDispatcherTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private const int AppId = 2429270;
    private static readonly DateTimeOffset ExtractedAt =
        new(2026, 8, 18, 9, 30, 0, TimeSpan.Zero);

    private static string Message(int appId = AppId, string? name = "The RPG") => $$"""
        {
          "app_id": {{appId}},
          "game_name": {{(name is null ? "null" : $"\"{name}\"")}},
          "reddit_id": "abc123",
          "extracted_at": "{{ExtractedAt:O}}",
          "source": "reddit",
          "schema_version": 1
        }
        """;

    private static GameAnalyzedDispatcher NewDispatcher(CatoDbContext db, StubRedisSync? redis = null) =>
        new(db, redis ?? new StubRedisSync(), NullLogger<GameAnalyzedDispatcher>.Instance);

    [Fact]
    public async Task Unknown_game_is_created_and_marked_analysed()
    {
        await using (var db = NewContext())
            await NewDispatcher(db).DispatchAsync(Message(), default);

        await using var check = NewContext();
        var game = await check.Games.SingleAsync(g => g.AppId == AppId);

        Assert.Equal("The RPG", game.Name);
        Assert.Equal("Other", game.GameType);
        Assert.Equal(ExtractedAt.UtcDateTime, game.AnalyzedAt);
        Assert.Null(game.LastEnrichedAt);
    }

    [Fact]
    public async Task A_dispatched_game_goes_to_the_front_of_the_enrichment_queue()
    {
        // A game CATO discovered on its own, waiting its turn by app id.
        await SeedAsync(g => { g.AppId = 10; g.Name = "Discovered"; });

        await using (var db = NewContext())
            await NewDispatcher(db).DispatchAsync(Message(), default);

        await using var db2 = NewContext();
        var batch = await GameEnrichmentQueue.SelectAsync(
            db2, new GameEnrichmentSettings { BatchSize = 10 });

        var first = await db2.Games.SingleAsync(g => g.AppId == AppId);
        Assert.Equal(first.Id, batch.GameIds[0]);
        Assert.Equal(1, batch.Analysed);
    }

    [Fact]
    public async Task An_existing_game_is_re_marked_without_losing_its_store_data()
    {
        var enrichedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedAsync(g =>
        {
            g.LastEnrichedAt = enrichedAt;
            g.ReleaseDateRaw = "Q4 2026";
        });

        await using (var db = NewContext())
            await NewDispatcher(db).DispatchAsync(Message(), default);

        await using var check = NewContext();
        var game = await check.Games.SingleAsync();

        Assert.Equal(ExtractedAt.UtcDateTime, game.AnalyzedAt);
        Assert.Equal(enrichedAt, game.LastEnrichedAt);
        Assert.Equal("Q4 2026", game.ReleaseDateRaw);
    }

    [Fact]
    public async Task An_already_enriched_game_is_not_re_queued_for_enrichment()
    {
        await SeedAsync(g => g.LastEnrichedAt = DateTime.UtcNow);

        await using (var db = NewContext())
            await NewDispatcher(db).DispatchAsync(Message(), default);

        await using var db2 = NewContext();
        var batch = await GameEnrichmentQueue.SelectAsync(
            db2, new GameEnrichmentSettings { BatchSize = 10 });

        Assert.Empty(batch.GameIds);
    }

    [Fact]
    public async Task The_follower_queue_priority_is_still_applied()
    {
        var redis = new StubRedisSync();

        await using (var db = NewContext())
            await NewDispatcher(db, redis).DispatchAsync(Message(), default);

        Assert.Equal([AppId], redis.Prioritized);
    }

    [Fact]
    public async Task Placeholder_name_is_replaced_but_a_steam_name_is_not()
    {
        await SeedAsync(g =>
        {
            g.Name = "App " + AppId;
            g.LastEnrichedAt = DateTime.UtcNow;
        });

        await using (var db = NewContext())
            await NewDispatcher(db).DispatchAsync(Message(name: "The RPG"), default);

        await using (var check = NewContext())
            Assert.Equal("The RPG", (await check.Games.SingleAsync()).Name);

        await using (var db = NewContext())
            await NewDispatcher(db).DispatchAsync(Message(name: "the rpg (reddit)"), default);

        await using (var check = NewContext())
            Assert.Equal("The RPG", (await check.Games.SingleAsync()).Name);
    }

    private async Task SeedAsync(Action<Game> configure)
    {
        var game = new Game
        {
            Id = Guid.NewGuid(),
            AppId = AppId,
            Name = "The RPG",
            GameType = "Other"
        };
        configure(game);

        await using var db = NewContext();
        db.Games.Add(game);
        await db.SaveChangesAsync();
    }

    private sealed class StubRedisSync : IRedisAppIdSyncService
    {
        public List<int> Prioritized { get; } = [];

        public Task PrioritizeFollowerHistoryAsync(int appId, DateTimeOffset analyzedAt, CancellationToken ct)
        {
            Prioritized.Add(appId);
            return Task.CompletedTask;
        }

        public Task SyncAsync(int appId, string gameType, string? name, CancellationToken ct)
            => Task.CompletedTask;

        public Task UpdateAsync(int appId, string oldType, string newType, string? name, CancellationToken ct)
            => Task.CompletedTask;

        public Task RemoveAsync(int appId, CancellationToken ct) => Task.CompletedTask;
    }
}
