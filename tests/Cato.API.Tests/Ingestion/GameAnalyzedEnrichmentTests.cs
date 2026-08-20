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
/// Covers the enrichment half of <see cref="GameAnalyzedDispatcher"/>: a game reddit
/// surfaced should come out of this with its store record, and a Steam failure should
/// cost nothing but the enrichment.
/// </summary>
public class GameAnalyzedEnrichmentTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public GameAnalyzedEnrichmentTests()
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

    private static GameAnalyzedDispatcher NewDispatcher(
        CatoDbContext db, StubEnrichment enrichment, StubRedisSync? redis = null) =>
        new(db, redis ?? new StubRedisSync(), enrichment,
            NullLogger<GameAnalyzedDispatcher>.Instance);

    [Fact]
    public async Task Stubbed_game_is_enriched_and_marked_analysed()
    {
        var enrichment = new StubEnrichment();

        await using (var db = NewContext())
            await NewDispatcher(db, enrichment).DispatchAsync(Message(), default);

        await using var check = NewContext();
        var game = await check.Games.SingleAsync(g => g.AppId == AppId);

        Assert.Equal([game.Id], enrichment.Enriched);
        Assert.Equal(ExtractedAt.UtcDateTime, game.AnalyzedAt);
    }

    [Fact]
    public async Task Already_enriched_game_is_not_enriched_again()
    {
        await SeedAsync(g => g.LastEnrichedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        var enrichment = new StubEnrichment();
        await using (var db = NewContext())
            await NewDispatcher(db, enrichment).DispatchAsync(Message(), default);

        Assert.Empty(enrichment.Enriched);

        // The event still records that reddit looked at it again.
        await using var check = NewContext();
        Assert.Equal(ExtractedAt.UtcDateTime, (await check.Games.SingleAsync()).AnalyzedAt);
    }

    [Fact]
    public async Task Game_past_the_inline_failure_ceiling_is_left_to_the_watcher()
    {
        await SeedAsync(g => g.EnrichmentFailures = 3);

        var enrichment = new StubEnrichment();
        await using (var db = NewContext())
            await NewDispatcher(db, enrichment).DispatchAsync(Message(), default);

        Assert.Empty(enrichment.Enriched);
    }

    [Fact]
    public async Task Steam_failure_does_not_fail_the_message()
    {
        var enrichment = new StubEnrichment { Throw = new HttpRequestException("Steam is down") };
        var redis = new StubRedisSync();

        await using (var db = NewContext())
            await NewDispatcher(db, enrichment, redis).DispatchAsync(Message(), default);

        // The row and the follower-queue priority — the parts that do not depend on
        // Steam answering — must still be there.
        await using var check = NewContext();
        Assert.Equal(AppId, (await check.Games.SingleAsync()).AppId);
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
            await NewDispatcher(db, new StubEnrichment()).DispatchAsync(Message(name: "The RPG"), default);

        await using (var check = NewContext())
            Assert.Equal("The RPG", (await check.Games.SingleAsync()).Name);

        await using (var db = NewContext())
            await NewDispatcher(db, new StubEnrichment()).DispatchAsync(Message(name: "the rpg (reddit)"), default);

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

    private sealed class StubEnrichment : ISteamGameEnrichmentService
    {
        public List<Guid> Enriched { get; } = [];
        public Exception? Throw { get; init; }
        public bool Result { get; init; } = true;

        public Task<bool> EnrichGameAsync(Guid gameId, CancellationToken ct = default)
        {
            if (Throw is not null) throw Throw;
            Enriched.Add(gameId);
            return Task.FromResult(Result);
        }
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
