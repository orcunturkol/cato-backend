using Cato.API.Models.Ingestion;
using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers the follower-history queue: which games the sweep (re)queues and at what
/// score, how a score reads back as a status, and how the follower series is merged
/// from the SteamDB graph, the SteamDB list values and the group scrape.
/// </summary>
public class FollowerHistoryQueueTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public FollowerHistoryQueueTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);
    private static readonly FollowerHistorySweepSettings Settings = new();

    private static double FetchedAgo(TimeSpan age) => (Now - age).ToUnixTimeSeconds();

    private static FollowerHistoryCandidates Candidates(int[]? owned = null, Dictionary<int, int>? ranks = null) =>
        new((owned ?? []).ToHashSet(), ranks ?? new Dictionary<int, int>());

    private static Dictionary<int, double> Plan(
        FollowerHistoryCandidates candidates,
        Dictionary<int, double>? scores = null,
        HashSet<int>? quarantined = null) =>
        FollowerHistoryQueue.Plan(candidates, scores ?? new(), quarantined ?? [], Now, Settings)
            .ToDictionary(w => w.Key, w => w.Value);

    // ── Plan ──

    [Fact]
    public void A_list_game_never_fetched_is_queued_at_its_rank()
    {
        var writes = Plan(Candidates(ranks: new() { [10] = 7 }));

        Assert.Equal(FollowerHistoryQueue.ListScore(7), writes[10]);
    }

    [Fact]
    public void A_better_rank_sorts_ahead_and_both_sort_behind_owned_and_requests()
    {
        Assert.True(FollowerHistoryQueue.ListScore(1) < FollowerHistoryQueue.ListScore(2));
        Assert.True(FollowerHistoryQueue.OwnedScore < FollowerHistoryQueue.ListScore(1));
        Assert.True(FollowerHistoryQueue.RequestScore(Now) < FollowerHistoryQueue.RequestBandMax);
        Assert.True(FollowerHistoryQueue.RequestBandMax < FollowerHistoryQueue.OwnedScore);
        Assert.True(FollowerHistoryQueue.ListScore(10_000) < 0);
    }

    [Fact]
    public void A_seeded_game_in_a_list_moves_into_the_list_band()
    {
        var writes = Plan(Candidates(ranks: new() { [10] = 3 }), scores: new() { [10] = 0 });

        Assert.Equal(FollowerHistoryQueue.ListScore(3), writes[10]);
    }

    [Fact]
    public void A_requested_game_is_never_demoted()
    {
        var writes = Plan(
            Candidates(owned: [1], ranks: new() { [2] = 5 }),
            scores: new() { [1] = FollowerHistoryQueue.RequestScore(Now), [2] = FollowerHistoryQueue.RequestScore(Now) });

        Assert.Empty(writes);
    }

    [Fact]
    public void A_quarantined_game_is_left_alone()
    {
        var writes = Plan(Candidates(owned: [1], ranks: new() { [2] = 5 }), quarantined: [1, 2]);

        Assert.Empty(writes);
    }

    [Fact]
    public void An_owned_game_in_a_list_takes_the_owned_band()
    {
        var writes = Plan(Candidates(owned: [1], ranks: new() { [1] = 1 }));

        Assert.Equal(FollowerHistoryQueue.OwnedScore, writes[1]);
    }

    [Fact]
    public void An_owned_game_in_no_list_is_refreshed_daily()
    {
        var writes = Plan(
            Candidates(owned: [1, 2]),
            scores: new() { [1] = FetchedAgo(TimeSpan.FromHours(10)), [2] = FetchedAgo(TimeSpan.FromHours(21)) });

        Assert.False(writes.ContainsKey(1));
        Assert.Equal(FollowerHistoryQueue.OwnedScore, writes[2]);
    }

    [Fact]
    public void A_listed_owned_game_waits_for_the_list_refresh_age_and_keeps_the_owned_band()
    {
        var writes = Plan(
            Candidates(owned: [1, 2], ranks: new() { [1] = 5, [2] = 6 }),
            scores: new() { [1] = FetchedAgo(TimeSpan.FromHours(21)), [2] = FetchedAgo(TimeSpan.FromDays(31)) });

        Assert.False(writes.ContainsKey(1));
        Assert.Equal(FollowerHistoryQueue.OwnedScore, writes[2]);
    }

    [Fact]
    public void A_list_game_is_refreshed_only_after_its_refresh_age()
    {
        var writes = Plan(
            Candidates(ranks: new() { [1] = 1, [2] = 2 }),
            scores: new() { [1] = FetchedAgo(TimeSpan.FromDays(10)), [2] = FetchedAgo(TimeSpan.FromDays(31)) });

        Assert.False(writes.ContainsKey(1));
        Assert.Equal(FollowerHistoryQueue.ListScore(2), writes[2]);
    }

    [Fact]
    public void A_score_already_at_its_target_is_not_written_again()
    {
        var writes = Plan(
            Candidates(owned: [1], ranks: new() { [2] = 4 }),
            scores: new() { [1] = FollowerHistoryQueue.OwnedScore, [2] = FollowerHistoryQueue.ListScore(4) });

        Assert.Empty(writes);
    }

    [Fact]
    public void A_queued_list_game_follows_its_new_rank()
    {
        var writes = Plan(
            Candidates(ranks: new() { [2] = 1 }),
            scores: new() { [2] = FollowerHistoryQueue.ListScore(40) });

        Assert.Equal(FollowerHistoryQueue.ListScore(1), writes[2]);
    }

    // ── Progress ──

    [Fact]
    public void Progress_counts_done_and_waiting_games()
    {
        var progress = FollowerHistoryQueue.Progress(
            Candidates(owned: [1, 2, 3, 4], ranks: new() { [1] = 1, [10] = 1, [11] = 2, [12] = 3, [13] = 4 }),
            scores: new Dictionary<int, double>
            {
                [1] = FetchedAgo(TimeSpan.FromHours(2)),        // owned, done (also listed: counts as owned)
                [2] = FollowerHistoryQueue.OwnedScore,          // owned, waiting
                [3] = FetchedAgo(TimeSpan.FromHours(30)),       // owned, done until the sweep queues it
                [10] = FetchedAgo(TimeSpan.FromDays(3)),        // listed, done
                [11] = FollowerHistoryQueue.ListScore(2),       // listed, waiting
                [12] = FollowerHistoryQueue.RequestScore(Now),  // listed, requested: waiting
                [13] = FetchedAgo(TimeSpan.FromDays(1)),        // quarantined below
            },
            quarantined: new HashSet<int> { 4, 13 });

        Assert.Equal(new FollowerHistoryProgress(OwnedDone: 2, OwnedWaiting: 1, ListedDone: 1, ListedWaiting: 2), progress);
    }

    // ── Describe ──

    [Fact]
    public void Scores_read_back_as_a_status()
    {
        Assert.Equal("not_queued", FollowerHistoryQueue.Describe(null, null).Status);
        Assert.Equal("queued", FollowerHistoryQueue.Describe(0, null).Status);
        Assert.Equal("queued", FollowerHistoryQueue.Describe(FollowerHistoryQueue.ListScore(3), null).Status);
        Assert.Equal("queued", FollowerHistoryQueue.Describe(FollowerHistoryQueue.OwnedScore, null).Status);

        var requested = FollowerHistoryQueue.Describe(FollowerHistoryQueue.RequestScore(Now), null);
        Assert.Equal("requested", requested.Status);
        Assert.Equal(Now, requested.RequestedAt);

        var fetched = FollowerHistoryQueue.Describe(Now.ToUnixTimeSeconds(), null);
        Assert.Equal("fetched", fetched.Status);
        Assert.Equal(Now, fetched.LastFetchedAt);

        var quarantined = FollowerHistoryQueue.Describe(null, Now.ToUnixTimeSeconds());
        Assert.Equal("quarantined", quarantined.Status);
        Assert.Equal(Now, quarantined.QuarantinedAt);
    }

    // ── LoadCandidatesAsync ──

    private async Task<Game> AddGameAsync(CatoDbContext db, int appId, string type = "Other")
    {
        var game = new Game { Id = Guid.NewGuid(), AppId = appId, Name = $"Game {appId}", GameType = type };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game;
    }

    private static SteamDbSnapshot Listed(Game game, DateOnly date, string source, int rank, int follows = 0) => new()
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        SnapshotDate = date,
        Source = source,
        Rank = rank,
        Follows = follows,
        ScrapedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Candidates_are_owned_games_and_recent_list_games_at_their_best_rank()
    {
        await using (var db = NewContext())
        {
            await AddGameAsync(db, 1, "Owned");
            await AddGameAsync(db, 2, "Sourcing");
            var both = await AddGameAsync(db, 3);
            var old = await AddGameAsync(db, 4);

            db.SteamDbSnapshots.AddRange(
                Listed(both, Today, "steamdb_most_wished", 900),
                Listed(both, Today.AddDays(-2), "steamdb_wishlist_activity", 40),
                Listed(old, Today.AddDays(-8), "steamdb_most_wished", 1));
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var candidates = await FollowerHistoryQueue.LoadCandidatesAsync(read, Settings, Today);

        Assert.Equal([1], candidates.Owned);
        Assert.Equal(new Dictionary<int, int> { [3] = 40 }, candidates.ListRanks);
    }

    // ── GetFollowerSeriesAsync ──

    private static GroupMemberCountSnapshot Member(Game game, DateOnly date, string source, int count, string? error = null) => new()
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        SnapshotDate = date,
        Source = source,
        MemberCount = count,
        Error = error,
        ScrapedAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
    };

    private static readonly DateOnly D1 = new(2026, 10, 1);

    private async Task<List<FollowerPointDto>> SeriesAsync(int appId)
    {
        await using var db = NewContext();
        return await new GameDataService(db).GetFollowerSeriesAsync(new GetFollowerSeriesQuery(appId));
    }

    [Fact]
    public async Task The_graph_leads_and_list_then_group_values_fill_the_days_after_it()
    {
        await using (var db = NewContext())
        {
            var game = await AddGameAsync(db, 10);
            const string graph = GroupMemberCountSnapshot.SteamDbFollowerSource;
            const string group = GroupMemberCountSnapshot.SteamCommunitySource;
            db.GroupMemberCountSnapshots.AddRange(
                Member(game, D1, graph, 100),
                Member(game, D1.AddDays(1), graph, 110),
                Member(game, D1.AddDays(1), group, 999),               // inside the graph: ignored
                Member(game, D1.AddDays(3), group, 130),               // list has this day: list wins
                Member(game, D1.AddDays(4), group, 140),               // no list value: group fills
                Member(game, D1.AddDays(5), group, 0, error: "429"));  // failed scrape: ignored
            db.SteamDbSnapshots.AddRange(
                Listed(game, D1.AddDays(1), "steamdb_most_wished", 5, follows: 555), // inside the graph: ignored
                Listed(game, D1.AddDays(2), "steamdb_most_wished", 5, follows: 120),
                Listed(game, D1.AddDays(2), "steamdb_wishlist_activity", 9, follows: 120),
                Listed(game, D1.AddDays(3), "steamdb_most_wished", 5, follows: 131));
            await db.SaveChangesAsync();
        }

        var series = await SeriesAsync(10);

        Assert.Equal(
            [
                new FollowerPointDto(D1, 100, "steamdb_follower_history"),
                new FollowerPointDto(D1.AddDays(1), 110, "steamdb_follower_history"),
                new FollowerPointDto(D1.AddDays(2), 120, "steamdb_list"),
                new FollowerPointDto(D1.AddDays(3), 131, "steamdb_list"),
                new FollowerPointDto(D1.AddDays(4), 140, "steam_community_group"),
            ],
            series);
    }

    [Fact]
    public async Task Without_a_graph_the_list_values_are_the_whole_series()
    {
        await using (var db = NewContext())
        {
            var game = await AddGameAsync(db, 11);
            db.SteamDbSnapshots.AddRange(
                Listed(game, D1, "steamdb_most_wished", 5, follows: 50),
                Listed(game, D1.AddDays(1), "steamdb_most_wished", 5, follows: 0));  // no value
            await db.SaveChangesAsync();
        }

        Assert.Equal([new FollowerPointDto(D1, 50, "steamdb_list")], await SeriesAsync(11));
    }

    [Fact]
    public async Task An_unknown_game_has_an_empty_series()
    {
        Assert.Empty(await SeriesAsync(12345));
    }
}
