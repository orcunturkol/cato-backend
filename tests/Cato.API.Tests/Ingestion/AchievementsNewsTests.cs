using Cato.API.Models.Ingestion;
using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers the keyless public stats the watchers store: daily achievement unlock
/// percentages (joined with game_achievement_schema on read) and game news (upsert by
/// gid, excerpt without markup on read). Figures are from 2390230 on 2026-10-20.
/// </summary>
public class AchievementsNewsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public AchievementsNewsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private static readonly Guid GameId = Guid.NewGuid();
    private const int AppId = 2390230;
    private static readonly DateOnly Oct20 = new(2026, 10, 20);

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        db.Games.Add(new Game { Id = GameId, AppId = AppId, Name = "The Chef's Shift", GameType = "Owned" });
        db.GameAchievementSchemas.Add(new GameAchievementSchema { Id = Guid.NewGuid(), GameId = GameId, AppId = AppId, ApiName = "FirstUpgrade", DisplayName = "First Upgrade" });
        db.GameAchievementSchemas.Add(new GameAchievementSchema { Id = Guid.NewGuid(), GameId = GameId, AppId = AppId, ApiName = "CompleteChapter1", DisplayName = "Chapter 1", Hidden = true });
        db.GameAchievementSchemas.Add(new GameAchievementSchema { Id = Guid.NewGuid(), GameId = GameId, AppId = AppId, ApiName = "Secret", DisplayName = "Secret" });
        await db.SaveChangesAsync();
    }

    private static SteamAchievementPercent P(string name, decimal percent) => new(name, percent);

    private async Task<(int, int)> UpsertPercentsAsync(DateOnly date, params SteamAchievementPercent[] percents)
    {
        await using var db = NewContext();
        var result = await SteamPublicStatsStore.UpsertAchievementPercentsAsync(db, GameId, AppId, date, percents, CancellationToken.None);
        await db.SaveChangesAsync();
        return result;
    }

    [Fact]
    public async Task PercentsAreADailySnapshotUpdatedWithinTheDay()
    {
        await SeedAsync();
        var first = await UpsertPercentsAsync(Oct20, P("FirstUpgrade", 95.0m), P("CompleteChapter1", 71.5m), P("Bad", 140m));
        var again = await UpsertPercentsAsync(Oct20, P("FirstUpgrade", 95.0m), P("CompleteChapter1", 71.6m));
        var nextDay = await UpsertPercentsAsync(Oct20.AddDays(1), P("FirstUpgrade", 95.1m));

        Assert.Equal((2, 0), first);
        Assert.Equal((0, 1), again);
        Assert.Equal((1, 0), nextDay);
        await using var db = NewContext();
        Assert.Equal(3, await db.GameAchievementPercents.CountAsync());
    }

    [Fact]
    public async Task AchievementsJoinTheSchemaWithTheLatestDay()
    {
        await SeedAsync();
        await UpsertPercentsAsync(Oct20, P("FirstUpgrade", 94.0m), P("CompleteChapter1", 70.0m));
        await UpsertPercentsAsync(Oct20.AddDays(1), P("FirstUpgrade", 95.0m), P("CompleteChapter1", 71.5m), P("NewOne", 1.2m));

        await using var db = NewContext();
        var dto = await new GameDataService(db).GetAchievementsAsync(new GetAchievementsQuery(AppId));

        Assert.Equal(Oct20.AddDays(1), dto.SnapshotDate);
        Assert.Equal(
            [("FirstUpgrade", (decimal?)95.0m), ("CompleteChapter1", 71.5m), ("NewOne", 1.2m), ("Secret", null)],
            dto.Achievements.Select(a => (a.ApiName, a.Percent)).ToList());
        Assert.True(dto.Achievements[1].Hidden);
        Assert.Null(dto.Achievements[2].DisplayName); // not in the schema yet
    }

    private static SteamNewsItem News(string gid, string title, long date, string contents = "") =>
        new(gid, title, $"https://store.steampowered.com/news/{gid}", "Catoptric", contents, "Community Announcements", date);

    [Fact]
    public async Task NewsIsUpsertedByGidAndReadNewestFirst()
    {
        await SeedAsync();
        await using (var db = NewContext())
        {
            var first = await SteamPublicStatsStore.UpsertNewsAsync(db, GameId,
                [News("1", "Cooking Fest Sale", 1_760_000_000, "[h2]Sale![/h2] <b>40%</b> off {STEAM_CLAN_IMAGE}/abc.png now"), News("2", "Patch 1.1", 1_759_000_000)],
                CancellationToken.None);
            await db.SaveChangesAsync();
            Assert.Equal((2, 0), first);
        }
        await using (var db = NewContext())
        {
            var again = await SteamPublicStatsStore.UpsertNewsAsync(db, GameId,
                [News("1", "Cooking Fest Sale!", 1_760_000_000, "[h2]Sale![/h2] <b>40%</b> off {STEAM_CLAN_IMAGE}/abc.png now"), News("2", "Patch 1.1", 1_759_000_000)],
                CancellationToken.None);
            await db.SaveChangesAsync();
            Assert.Equal((0, 1), again);
        }

        await using var read = NewContext();
        var news = await new GameDataService(read).GetGameNewsAsync(new GetGameNewsQuery(AppId, 50));

        Assert.Equal(["Cooking Fest Sale!", "Patch 1.1"], news.Select(n => n.Title).ToList());
        Assert.Equal("Sale! 40% off now", news[0].Excerpt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_760_000_000).UtcDateTime, news[0].PublishedAt);
        Assert.Single(await new GameDataService(read).GetGameNewsAsync(new GetGameNewsQuery(AppId, 1)));
        Assert.All(await read.GameNews.ToListAsync(), n => Assert.Equal("news", n.Source));
    }
}
