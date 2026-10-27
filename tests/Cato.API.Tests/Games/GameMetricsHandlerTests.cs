using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using Cato.API.Services.Handlers.Games;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Games;

public class GameMetricsHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public GameMetricsHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;
        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<List<GameMetricsDto>> MetricsAsync(params int[] appIds)
    {
        await using var db = NewContext();
        return await new GetGameMetricsHandler(db, new FixedTime(Now))
            .Handle(new GetGameMetricsQuery(appIds), CancellationToken.None);
    }

    private static async Task<Game> AddGameAsync(CatoDbContext db, int appId)
    {
        var game = new Game { Id = Guid.NewGuid(), AppId = appId, Name = $"Game {appId}", GameType = "Other" };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    private static GroupMemberCountSnapshot Member(Game game, DateOnly date, string source, int count) => new()
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        SnapshotDate = date,
        Source = source,
        MemberCount = count,
        ScrapedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
    };

    private static SteamDbSnapshot Listed(Game game, DateOnly date, string source, int rank, int follows, int gain = 0) => new()
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        SnapshotDate = date,
        Source = source,
        Rank = rank,
        Follows = follows,
        SevenDayGain = gain,
        ScrapedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Followers_match_the_followers_endpoint_cut_to_the_window()
    {
        await using (var db = NewContext())
        {
            var game = await AddGameAsync(db, 10);
            const string graph = GroupMemberCountSnapshot.SteamDbFollowerSource;
            const string group = GroupMemberCountSnapshot.SteamCommunitySource;
            for (var day = 45; day >= 10; day--)
                db.GroupMemberCountSnapshots.Add(Member(game, Today.AddDays(-day), graph, 1000 + (45 - day) * 10));
            for (var day = 12; day >= 0; day--)
                db.GroupMemberCountSnapshots.Add(Member(game, Today.AddDays(-day), group, 2000 + day));
            for (var day = 9; day >= 4; day--)
                db.SteamDbSnapshots.Add(Listed(game, Today.AddDays(-day), "steamdb_most_wished", 500, 1500 + day));
            await db.SaveChangesAsync();
        }

        var metrics = Assert.Single(await MetricsAsync(10));

        List<FollowerPointDto> full;
        await using (var db = NewContext())
            full = await new GameDataService(db).GetFollowerSeriesAsync(new GetFollowerSeriesQuery(10));
        var expected = full.Where(p => p.Date >= Today.AddDays(-30)).ToList();

        Assert.Equal(expected, metrics.Followers);
        // Graph up to day -10, list values for -9..-4, the group scrape for -3..0.
        Assert.Equal("steamdb_follower_history", metrics.Followers.Single(p => p.Date == Today.AddDays(-10)).Source);
        Assert.Equal("steamdb_list", metrics.Followers.Single(p => p.Date == Today.AddDays(-5)).Source);
        Assert.Equal("steam_community_group", metrics.Followers[^1].Source);
    }

    [Fact]
    public async Task Ranks_are_the_newest_row_per_list_within_thirty_days()
    {
        await using (var db = NewContext())
        {
            var game = await AddGameAsync(db, 20);
            db.SteamDbSnapshots.AddRange(
                Listed(game, Today.AddDays(-3), "steamdb_most_wished", 900, 15000, 700),
                Listed(game, Today.AddDays(-1), "steamdb_most_wished", 812, 15400, 900),
                Listed(game, Today.AddDays(-40), "steamdb_wishlist_activity", 12, 15000));
            await db.SaveChangesAsync();
        }

        var metrics = Assert.Single(await MetricsAsync(20));

        Assert.Equal(new ListRankDto(Today.AddDays(-1), 812, 15400, 900), metrics.MostWished);
        Assert.Null(metrics.WishlistActivity);
    }

    [Fact]
    public async Task Ccu_shows_the_latest_value_and_the_seven_day_peak()
    {
        await using (var db = NewContext())
        {
            var game = await AddGameAsync(db, 30);
            db.CcuHistories.AddRange(
                new CcuHistory { Id = Guid.NewGuid(), GameId = game.Id, Timestamp = Now.UtcDateTime.AddDays(-8), CcuCount = 500 },
                new CcuHistory { Id = Guid.NewGuid(), GameId = game.Id, Timestamp = Now.UtcDateTime.AddDays(-2), CcuCount = 30, PeakCcuToday = 45 },
                new CcuHistory { Id = Guid.NewGuid(), GameId = game.Id, Timestamp = Now.UtcDateTime.AddHours(-1), CcuCount = 12 });
            await db.SaveChangesAsync();
        }

        var metrics = Assert.Single(await MetricsAsync(30));

        Assert.Equal(12, metrics.Ccu!.Latest);
        Assert.Equal(45, metrics.Ccu.Peak7d);
        Assert.Equal(Now.UtcDateTime.AddHours(-1), metrics.Ccu.LatestAt);
    }

    [Fact]
    public async Task Unknown_games_are_left_out_and_games_without_data_come_back_empty()
    {
        await using (var db = NewContext())
            await AddGameAsync(db, 40);

        var metrics = Assert.Single(await MetricsAsync(40, 999));

        Assert.Equal(40, metrics.AppId);
        Assert.Empty(metrics.Followers);
        Assert.Null(metrics.MostWished);
        Assert.Null(metrics.Ccu);
        Assert.Null(metrics.FollowersSince);
        Assert.Null(metrics.Reviews);
        Assert.Empty(metrics.ReviewsDaily!);
        Assert.Null(metrics.Achievements);
    }

    [Fact]
    public async Task Followers_since_is_the_first_day_of_the_steamdb_graph()
    {
        await using (var db = NewContext())
        {
            var graphed = await AddGameAsync(db, 50);
            var scraped = await AddGameAsync(db, 51);
            db.GroupMemberCountSnapshots.AddRange(
                Member(graphed, Today.AddDays(-400), GroupMemberCountSnapshot.SteamDbFollowerSource, 10),
                Member(graphed, Today.AddDays(-3), GroupMemberCountSnapshot.SteamDbFollowerSource, 900),
                Member(graphed, Today.AddDays(-1), GroupMemberCountSnapshot.SteamCommunitySource, 950),
                Member(scraped, Today.AddDays(-20), GroupMemberCountSnapshot.SteamCommunitySource, 300));
            await db.SaveChangesAsync();
        }

        var metrics = (await MetricsAsync(50, 51)).ToDictionary(m => m.AppId);

        Assert.Equal(Today.AddDays(-400), metrics[50].FollowersSince);
        Assert.Null(metrics[51].FollowersSince); // the scrape starts when CATO started following, not with the page
    }

    private static SteamReview Review(Game game, string id, DateTime created, bool up) => new()
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        RecommendationId = id,
        Language = "english",
        VotedUp = up,
        TimestampCreated = created,
        TimestampUpdated = created,
    };

    [Fact]
    public async Task Reviews_show_the_newest_summary_and_the_reviews_per_day()
    {
        await using (var db = NewContext())
        {
            var game = await AddGameAsync(db, 60);
            db.ReviewSummarySnapshots.AddRange(
                new ReviewSummarySnapshot
                {
                    Id = Guid.NewGuid(), GameId = game.Id, SnapshotDate = Today.AddDays(-5), ReviewScoreDesc = "Positive",
                    TotalPositive = 80, TotalNegative = 10, TotalReviews = 90,
                },
                new ReviewSummarySnapshot
                {
                    Id = Guid.NewGuid(), GameId = game.Id, SnapshotDate = Today.AddDays(-1), ReviewScoreDesc = "Mostly Positive",
                    TotalPositive = 100, TotalNegative = 20, TotalReviews = 120,
                });
            var day1 = Now.UtcDateTime.AddDays(-2);
            db.SteamReviews.AddRange(
                Review(game, "r1", day1, up: true),
                Review(game, "r2", day1.AddHours(1), up: true),
                Review(game, "r3", day1.AddHours(2), up: false),
                Review(game, "r4", Now.UtcDateTime.AddDays(-1), up: true),
                Review(game, "r5", Now.UtcDateTime.AddDays(-60), up: true)); // before the window
            await db.SaveChangesAsync();
        }

        var metrics = Assert.Single(await MetricsAsync(60));

        Assert.Equal(new ReviewSummaryDto(Today.AddDays(-1), 120, 100, 20, "Mostly Positive"), metrics.Reviews);
        Assert.Equal(
            [new ReviewDayDto(Today.AddDays(-2), 3, 2), new ReviewDayDto(Today.AddDays(-1), 1, 1)],
            metrics.ReviewsDaily!);
    }

    [Fact]
    public async Task Achievements_summarize_the_newest_day_of_unlock_percentages()
    {
        await using (var db = NewContext())
        {
            var game = await AddGameAsync(db, 70);
            GameAchievementPercent Unlock(DateOnly day, string name, decimal percent) => new()
            {
                Id = Guid.NewGuid(), GameId = game.Id, AppId = 70, ApiName = name, SnapshotDate = day, Percent = percent,
            };
            db.GameAchievementPercents.AddRange(
                Unlock(Today.AddDays(-2), "a1", 90m),
                Unlock(Today.AddDays(-1), "a1", 80.5m),
                Unlock(Today.AddDays(-1), "a2", 40m),
                Unlock(Today.AddDays(-1), "a3", 20m),
                Unlock(Today.AddDays(-1), "a4", 5m));
            await db.SaveChangesAsync();
        }

        var metrics = Assert.Single(await MetricsAsync(70));

        Assert.Equal(new AchievementSummaryDto(Today.AddDays(-1), 4, 80.5, 30), metrics.Achievements);
    }
}
