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
    public async Task Capsule_is_the_main_capsule_and_falls_back_to_the_small_one()
    {
        // App details only has the 231x87 capsule; the store page shows the 616x353 one.
        var withMain = await SeedAsync(600);
        var withoutMain = await SeedAsync(601);
        const string small = "https://cdn.example/apps/601/capsule_231x87.jpg";
        const string main = "https://cdn.example/apps/600/abc/capsule_616x353.jpg";
        _steam.Data[600] = new SteamAppData { Name = "A", CapsuleImage = "https://cdn.example/apps/600/def/capsule_231x87.jpg" };
        _steam.Data[601] = new SteamAppData { Name = "B", CapsuleImage = small };
        _steam.MainCapsules[600] = main;

        await using (var db = NewContext())
        {
            Assert.True(await NewService(db).EnrichGameAsync(withMain));
            Assert.True(await NewService(db).EnrichGameAsync(withoutMain));
        }

        await using var check = NewContext();
        Assert.Equal(main, (await check.Games.SingleAsync(g => g.Id == withMain)).CapsuleImageUrl);
        Assert.Equal(small, (await check.Games.SingleAsync(g => g.Id == withoutMain)).CapsuleImageUrl);
    }

    [Fact]
    public async Task User_tag_named_like_a_category_keeps_both_rows_and_its_rank()
    {
        // The store lists "Co-op" as a category and as the top user tag; one shared name set
        // used to drop the user tag, so readers of TagType "UserTag" lost ranks 1 and 2.
        var gameId = await SeedAsync(500);
        var data = StoreData("Tagged", "Action");
        data.Categories =
        [
            new SteamCategory { Id = 1, Description = "Single-player" },
            new SteamCategory { Id = 2, Description = "Co-op" },
            new SteamCategory { Id = 3, Description = "Online Co-op" },
            new SteamCategory { Id = 4, Description = "online co-op" },
        ];
        _steam.Data[500] = data;
        _steam.UserTags[500] =
        [
            new SteamUserTag { Name = "Co-op", Rank = 1 },
            new SteamUserTag { Name = "Online Co-Op", Rank = 2 },
            new SteamUserTag { Name = "Survival", Rank = 3 },
            new SteamUserTag { Name = "survival", Rank = 4 },
        ];

        for (var run = 0; run < 2; run++)
        {
            await using (var db = NewContext())
                Assert.True(await NewService(db).EnrichGameAsync(gameId));

            await using var check = NewContext();
            var tags = await check.GenreTags.Where(t => t.GameId == gameId).ToListAsync();
            Assert.Equal(
                ["Co-op", "Online Co-op", "Single-player"],
                tags.Where(t => t.TagType == "Mechanic").Select(t => t.TagName).Order());
            Assert.All(tags.Where(t => t.TagType == "Mechanic"), t => Assert.Equal(0, t.Weight));
            Assert.Equal(
                [("Co-op", 1), ("Online Co-Op", 2), ("Survival", 3)],
                tags.Where(t => t.TagType == "UserTag").OrderBy(t => t.Weight).Select(t => (t.TagName, t.Weight)));
        }
    }

    [Fact]
    public async Task The_schema_allows_one_name_once_per_type()
    {
        var gameId = await SeedAsync(600);
        GenreTag Tag(string type) => new() { Id = Guid.NewGuid(), GameId = gameId, TagName = "Co-op", TagType = type };

        await using (var db = NewContext())
        {
            db.GenreTags.AddRange(Tag("Mechanic"), Tag("UserTag"));
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            db.GenreTags.Add(Tag("UserTag"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Two_enrichments_of_one_game_run_one_after_the_other()
    {
        // The PICS watcher and the enrichment watcher picked the same new stub; both replaced its
        // genres and the second insert broke IX_game_genre_GameId_GenreName_Source.
        var gameId = await SeedAsync(400);
        _steam.Data[400] = StoreData("Raced", "Action", "Indie");
        var calls = 0;
        var firstInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _steam.BeforeDetails = async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstInside.SetResult();
                await release.Task;
            }
        };

        await using var dbA = NewContext();
        await using var dbB = NewContext();
        var first = NewService(dbA).EnrichGameAsync(gameId);
        await firstInside.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = NewService(dbB).EnrichGameAsync(gameId);
        await Task.Delay(300);
        Assert.Equal(1, Volatile.Read(ref calls));

        release.SetResult();
        Assert.True(await first);
        Assert.True(await second);
        Assert.Equal(2, calls);

        await using var check = NewContext();
        Assert.Equal(["Action", "Indie"], await check.GameGenres.Where(g => g.GameId == gameId)
            .OrderBy(g => g.GenreName).Select(g => g.GenreName).ToListAsync());
    }

    [Fact]
    public async Task The_demo_the_store_page_links_to_is_kept_and_cleared_when_it_goes()
    {
        var gameId = await SeedAsync(300);
        var withDemo = StoreData("With Demo", "Simulation");
        withDemo.Demos = [new SteamDemo { AppId = 301, Description = "" }];
        _steam.Data[300] = withDemo;

        await using (var db = NewContext())
            Assert.True(await NewService(db).EnrichGameAsync(gameId));
        await using (var check = NewContext())
            Assert.Equal(301, (await check.Games.SingleAsync(g => g.Id == gameId)).DemoAppId);

        _steam.Data[300] = StoreData("With Demo", "Simulation");
        await using (var db = NewContext())
            Assert.True(await NewService(db).EnrichGameAsync(gameId));
        await using (var check = NewContext())
            Assert.Null((await check.Games.SingleAsync(g => g.Id == gameId)).DemoAppId);
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

        /// <summary>Runs before each store call; a test can hold the call open here.</summary>
        public Func<int, Task>? BeforeDetails { get; set; }

        public async Task<SteamAppData?> GetAppDetailsAsync(int appId, CancellationToken ct = default)
        {
            if (BeforeDetails is not null) await BeforeDetails(appId);
            return Data.GetValueOrDefault(appId);
        }

        public Task<IReadOnlyDictionary<int, SteamPriceOverview>> GetPriceOverviewsAsync(
            IReadOnlyList<int> appIds, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Dictionary<int, List<SteamUserTag>> UserTags { get; } = [];

        public Task<List<SteamUserTag>> GetUserTagsAsync(int appId, CancellationToken ct = default) =>
            Task.FromResult(UserTags.GetValueOrDefault(appId) ?? []);

        public Dictionary<int, string> MainCapsules { get; } = [];

        public Task<string?> GetMainCapsuleUrlAsync(int appId, CancellationToken ct = default) =>
            Task.FromResult(MainCapsules.GetValueOrDefault(appId));

        public Task<IReadOnlyDictionary<int, string>> GetMainCapsuleUrlsAsync(
            IReadOnlyList<int> appIds, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamAppReviewsResponse?> GetAppReviewsAsync(int appId, string cursor = "*", CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamPlayerSummariesResponse?> GetPlayerSummariesAsync(IReadOnlyList<long> steamIds, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamSchemaForGameResponse?> GetSchemaForGameAsync(int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamPlayerAchievementsResponse?> GetPlayerAchievementsAsync(long steamId64, int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SteamAchievementPercent>?> GetGlobalAchievementPercentagesAsync(int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SteamNewsItem>?> GetNewsForAppAsync(int appId, int count, CancellationToken ct = default) =>
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
