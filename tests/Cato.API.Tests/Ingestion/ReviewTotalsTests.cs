using Cato.API.Models.Games;
using Cato.API.Services.Handlers.Games;
using Cato.API.Validators.Games;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.Models;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers the review totals of listed games: which listed games a cycle reads (known to CATO,
/// not synced in full, stale or never read) and in what order, how a first review page becomes a
/// snapshot, and the list endpoint's command.
/// </summary>
public class ReviewTotalsTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 27);
    private static readonly SteamSettings Settings = new();
    private static readonly IReadOnlySet<int> NoSkip = new HashSet<int>();

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public ReviewTotalsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;
        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private async Task<Game> AddGameAsync(int appId, string gameType = "Other", DateOnly? snapshot = null)
    {
        await using var db = NewContext();
        var game = new Game { Id = Guid.NewGuid(), AppId = appId, Name = $"Game {appId}", GameType = gameType };
        db.Games.Add(game);
        if (snapshot is { } date)
            db.ReviewSummarySnapshots.Add(new ReviewSummarySnapshot
            {
                Id = Guid.NewGuid(), GameId = game.Id, SnapshotDate = date, ReviewScoreDesc = "Mixed", TotalReviews = 10,
            });
        await db.SaveChangesAsync();
        return game;
    }

    private async Task<ReviewTotalBatch> SelectAsync(
        IReadOnlyCollection<int> listed, SteamSettings? settings = null, IReadOnlySet<int>? skip = null)
    {
        await using var db = NewContext();
        return await ReviewTotals.SelectAsync(db, listed, settings ?? Settings, Today, skip ?? NoSkip);
    }

    private static SteamReviewQuerySummary Summary(string desc, int positive, int negative) => new()
    {
        ReviewScore = 5, ReviewScoreDesc = desc,
        TotalPositive = positive, TotalNegative = negative, TotalReviews = positive + negative,
    };

    [Fact]
    public async Task Listed_games_cato_knows_and_does_not_sync_in_full_are_due_when_stale_or_never_read()
    {
        await AddGameAsync(100);
        await AddGameAsync(200, "Competitor", snapshot: Today.AddDays(-30)); // exactly RefreshDays old
        await AddGameAsync(300, snapshot: Today.AddDays(-29));
        await AddGameAsync(400, "Sourcing");
        await AddGameAsync(500, "Owned");
        await AddGameAsync(600, "OGT");
        await AddGameAsync(700); // not listed

        var batch = await SelectAsync([100, 200, 300, 400, 500, 600, 999]); // 999: unknown to cato

        Assert.Equal([100, 200], batch.Games.Select(g => g.AppId));
        Assert.Equal(2, batch.Due);
        Assert.Null(batch.Games[0].LastSnapshot);
        Assert.Equal(Today.AddDays(-30), batch.Games[1].LastSnapshot);
    }

    [Fact]
    public async Task A_cycle_reads_games_never_read_first_then_the_stalest_up_to_the_batch_size()
    {
        await AddGameAsync(30);
        await AddGameAsync(10);
        await AddGameAsync(20);
        await AddGameAsync(5, snapshot: Today.AddDays(-40));
        await AddGameAsync(6, snapshot: Today.AddDays(-90));

        var batch = await SelectAsync([5, 6, 10, 20, 30], new SteamSettings { ReviewTotalsBatchSize = 4 });

        Assert.Equal([10, 20, 30, 6], batch.Games.Select(g => g.AppId));
        Assert.Equal(5, batch.Due);
    }

    [Fact]
    public async Task Games_that_failed_lately_wait_and_an_empty_list_reads_nothing()
    {
        await AddGameAsync(100);
        await AddGameAsync(200);

        var batch = await SelectAsync([100, 200], skip: new HashSet<int> { 100 });

        Assert.Equal([200], batch.Games.Select(g => g.AppId));
        Assert.Equal(0, (await SelectAsync([])).Due);
    }

    [Fact]
    public async Task A_summary_becomes_today_s_snapshot_and_the_game_s_review_fields()
    {
        var game = await AddGameAsync(100);
        var scarce = await AddGameAsync(200);

        await using (var db = NewContext())
        {
            var cycle = await ReviewTotals.ReadAsync(
                db,
                [new ReviewTotalDue(game.Id, 100, null), new ReviewTotalDue(scarce.Id, 200, null)],
                (appId, _) => Task.FromResult<SteamReviewQuerySummary?>(
                    appId == 100 ? Summary("Mostly Positive", 80, 20) : Summary("No user reviews", 0, 0)),
                Today);

            Assert.Equal(2, cycle.Read);
            Assert.Empty(cycle.FailedAppIds);
        }

        await using var check = NewContext();
        var snapshot = await check.ReviewSummarySnapshots.SingleAsync(s => s.GameId == game.Id);
        Assert.Equal((Today, 100, 80, 20), (snapshot.SnapshotDate, snapshot.TotalReviews, snapshot.TotalPositive, snapshot.TotalNegative));
        var stored = await check.Games.SingleAsync(g => g.Id == game.Id);
        Assert.Equal((100, "Mostly Positive"), (stored.ReviewCount, stored.SteamReviewScore));
        // No reviews yet is an answer too: stored as 0.
        Assert.Equal(0, (await check.ReviewSummarySnapshots.SingleAsync(s => s.GameId == scarce.Id)).TotalReviews);
    }

    [Fact]
    public async Task An_answer_without_a_summary_no_answer_or_an_error_is_a_failure_and_the_next_game_follows()
    {
        var empty = await AddGameAsync(100);
        var none = await AddGameAsync(200);
        var broken = await AddGameAsync(300);
        var fine = await AddGameAsync(400);

        await using (var db = NewContext())
        {
            var cycle = await ReviewTotals.ReadAsync(
                db,
                [new(empty.Id, 100, null), new(none.Id, 200, null), new(broken.Id, 300, null), new(fine.Id, 400, null)],
                (appId, _) => appId switch
                {
                    100 => Task.FromResult<SteamReviewQuerySummary?>(new SteamReviewQuerySummary()), // {"response":{}}
                    200 => Task.FromResult<SteamReviewQuerySummary?>(null),
                    300 => throw new HttpRequestException("connection reset"),
                    _ => Task.FromResult<SteamReviewQuerySummary?>(Summary("Positive", 9, 1)),
                },
                Today);

            Assert.Equal(1, cycle.Read);
            Assert.Equal([100, 200, 300], cycle.FailedAppIds);
        }

        await using var check = NewContext();
        Assert.Equal([fine.Id], await check.ReviewSummarySnapshots.Select(s => s.GameId).ToListAsync());
    }

    private sealed class FakeList : IReviewTotalList
    {
        public IReadOnlyCollection<int>? Replaced { get; private set; }

        public Task<int> ReplaceAsync(IReadOnlyCollection<int> appIds, CancellationToken ct)
        {
            Replaced = appIds;
            return Task.FromResult(appIds.Count);
        }

        public Task<IReadOnlyList<int>> GetAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<int>>([]);
    }

    [Fact]
    public async Task The_list_is_replaced_with_each_app_id_once()
    {
        var list = new FakeList();

        var result = await new SetReviewTotalListHandler(list)
            .Handle(new SetReviewTotalListCommand([3, 1, 3, 2]), CancellationToken.None);

        Assert.Equal([3, 1, 2], list.Replaced!);
        Assert.Equal(3, result.Listed);
    }

    [Fact]
    public void The_list_takes_positive_app_ids_and_may_be_empty()
    {
        var validator = new SetReviewTotalListValidator();

        Assert.True(validator.Validate(new SetReviewTotalListCommand([])).IsValid);
        Assert.True(validator.Validate(new SetReviewTotalListCommand([1, 2])).IsValid);
        Assert.False(validator.Validate(new SetReviewTotalListCommand([1, 0])).IsValid);
        Assert.False(validator.Validate(new SetReviewTotalListCommand(null!)).IsValid);
        Assert.False(validator.Validate(new SetReviewTotalListCommand(
            Enumerable.Range(1, SetReviewTotalListValidator.MaxAppIds + 1).ToList())).IsValid);
    }
}
