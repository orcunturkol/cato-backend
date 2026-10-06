using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers <see cref="SteamGameEnrichmentService"/> against store data the schema
/// rejects. The watcher enriches a whole cycle on one context, so one bad game used
/// to fail every game after it.
/// </summary>
public class SteamGameEnrichmentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;
    private readonly FakeSteamApi _steam = new();

    public SteamGameEnrichmentServiceTests()
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

    private SteamGameEnrichmentService NewService(CatoDbContext db) =>
        new(db, _steam, new NoOpRedisSync(), NullLogger<SteamGameEnrichmentService>.Instance);

    private async Task<Guid> SeedAsync(int appId)
    {
        var game = new Game { Id = Guid.NewGuid(), AppId = appId, Name = $"Game {appId}", GameType = "Other" };
        await using var db = NewContext();
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private static SteamAppData StoreData(string name, params string[] genres) => new()
    {
        Name = name,
        Genres = genres.Select((g, i) => new SteamGenre { Id = i.ToString(), Description = g }).ToList()
    };

    [Fact]
    public async Task Genre_listed_twice_by_the_store_is_stored_once()
    {
        // Real response for app 2885250 (DeathTower Demo).
        var gameId = await SeedAsync(2885250);
        _steam.Data[2885250] = StoreData("DeathTower Demo", "RPG", "Strategy", "Strategy");

        await using (var db = NewContext())
            Assert.True(await NewService(db).EnrichGameAsync(gameId));

        await using var check = NewContext();
        var genres = await check.GameGenres.Where(g => g.GameId == gameId)
            .OrderBy(g => g.GenreName).ToListAsync();
        Assert.Equal(["RPG", "Strategy"], genres.Select(g => g.GenreName));
        Assert.Equal("Primary", genres.Single(g => g.GenreName == "RPG").GenreType);
        Assert.NotNull((await check.Games.SingleAsync(g => g.Id == gameId)).LastEnrichedAt);
    }

    [Fact]
    public async Task Failed_save_does_not_fail_the_next_game_on_the_same_context()
    {
        var poisonId = await SeedAsync(100);
        var nextId = await SeedAsync(200);
        _steam.Data[100] = StoreData("Poison", "Poison");
        _steam.Data[200] = StoreData("Healthy", "Action");

        // Stand-in for any row the schema rejects.
        await using (var trigger = _connection.CreateCommand())
        {
            trigger.CommandText =
                """
                CREATE TRIGGER reject_poison BEFORE INSERT ON game_genre
                WHEN NEW."GenreName" = 'Poison' BEGIN SELECT RAISE(ABORT, 'rejected'); END;
                """;
            await trigger.ExecuteNonQueryAsync();
        }

        await using (var db = NewContext())
        {
            var service = NewService(db);
            await Assert.ThrowsAsync<DbUpdateException>(() => service.EnrichGameAsync(poisonId));
            Assert.True(await service.EnrichGameAsync(nextId));
        }

        await using var check = NewContext();
        var poison = await check.Games.SingleAsync(g => g.Id == poisonId);
        Assert.Null(poison.LastEnrichedAt);
        Assert.Equal(1, poison.EnrichmentFailures);
        Assert.NotNull((await check.Games.SingleAsync(g => g.Id == nextId)).LastEnrichedAt);
        Assert.Equal("Action", (await check.GameGenres.SingleAsync(g => g.GameId == nextId)).GenreName);
    }

    private sealed class FakeSteamApi : ISteamApiService
    {
        public Dictionary<int, SteamAppData> Data { get; } = [];

        public Task<SteamAppData?> GetAppDetailsAsync(int appId, CancellationToken ct = default) =>
            Task.FromResult(Data.GetValueOrDefault(appId));

        public Task<List<SteamUserTag>> GetUserTagsAsync(int appId, CancellationToken ct = default) =>
            Task.FromResult(new List<SteamUserTag>());

        public Task<SteamAppReviewsResponse?> GetAppReviewsAsync(int appId, string cursor = "*", CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamPlayerSummariesResponse?> GetPlayerSummariesAsync(IReadOnlyList<long> steamIds, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamSchemaForGameResponse?> GetSchemaForGameAsync(int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamPlayerAchievementsResponse?> GetPlayerAchievementsAsync(long steamId64, int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class NoOpRedisSync : IRedisAppIdSyncService
    {
        public Task SyncAsync(int appId, string gameType, string? name, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(int appId, string oldType, string newType, string? name, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveAsync(int appId, CancellationToken ct) => Task.CompletedTask;
        public Task PrioritizeFollowerHistoryAsync(int appId, DateTimeOffset analyzedAt, CancellationToken ct) => Task.CompletedTask;
    }
}
