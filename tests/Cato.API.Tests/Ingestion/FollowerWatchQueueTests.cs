using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers the follower watch: which games the sweep puts in the daily and weekly sets, and
/// how a sweep adds, moves and removes them.
/// </summary>
public class FollowerWatchQueueTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public FollowerWatchQueueTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static FollowerWatchCandidates Candidates(int[] daily, int[] weekly) =>
        new(daily.ToHashSet(), weekly.ToHashSet());

    private static FollowerWatchPlan Plan(
        FollowerWatchCandidates candidates,
        Dictionary<int, double>? daily = null,
        Dictionary<int, double>? weekly = null,
        HashSet<int>? quarantined = null) =>
        FollowerWatchQueue.Plan(candidates, daily ?? new(), weekly ?? new(), quarantined ?? []);

    // ── Plan ──

    [Fact]
    public void New_games_are_added_at_zero_so_the_next_run_reads_them_first()
    {
        var plan = Plan(Candidates([1], [2]));

        Assert.Equal([new(1, 0)], plan.AddDaily);
        Assert.Equal([new(2, 0)], plan.AddWeekly);
        Assert.Empty(plan.RemoveDaily);
        Assert.Empty(plan.RemoveWeekly);
    }

    [Fact]
    public void Games_already_in_their_set_keep_their_score()
    {
        var plan = Plan(Candidates([1], [2]), daily: new() { [1] = 1_700_000_000 }, weekly: new() { [2] = 1_600_000_000 });

        Assert.Empty(plan.AddDaily);
        Assert.Empty(plan.AddWeekly);
        Assert.Empty(plan.RemoveDaily);
        Assert.Empty(plan.RemoveWeekly);
    }

    [Fact]
    public void A_game_past_the_daily_window_moves_to_the_weekly_set_with_its_score()
    {
        var plan = Plan(Candidates([], [1]), daily: new() { [1] = 1_700_000_000 });

        Assert.Equal([new(1, 1_700_000_000)], plan.AddWeekly);
        Assert.Equal([1], plan.RemoveDaily);
    }

    [Fact]
    public void Games_that_left_the_candidates_are_removed()
    {
        var plan = Plan(Candidates([], []), daily: new() { [1] = 0 }, weekly: new() { [2] = 5 });

        Assert.Equal([1], plan.RemoveDaily);
        Assert.Equal([2], plan.RemoveWeekly);
    }

    [Fact]
    public void A_quarantined_game_is_not_added_again()
    {
        var plan = Plan(Candidates([1], [2]), quarantined: [1, 2]);

        Assert.Empty(plan.AddDaily);
        Assert.Empty(plan.AddWeekly);
    }

    // ── Candidates ──

    [Fact]
    public async Task Candidates_are_the_new_pages_games_that_are_not_tracked_split_by_age()
    {
        await using (var db = new CatoDbContext(_options))
        {
            db.Games.AddRange(
                Game(1, "Other", Now.AddDays(-10)),
                Game(2, "Other", Now.AddDays(-200)),
                Game(3, "Competitor", Now.AddDays(-5)),
                Game(4, "Sourcing", Now.AddDays(-5)),
                Game(5, "OGT", Now.AddDays(-5)),
                Game(6, "Owned", Now.AddDays(-5)),
                Game(7, "Other", Now.AddDays(-5), filtered: true),
                Game(8, "Other", Now.AddDays(-5), enriched: false),
                Game(9, "Other", Now.AddDays(-90)));
            await db.SaveChangesAsync();
            // SaveChanges stamps CreatedAt with the clock; set the ages afterwards.
            foreach (var (appId, days) in new[] { (1, 10), (2, 200), (3, 5), (4, 5), (5, 5), (6, 5), (7, 5), (8, 5), (9, 90) })
                await db.Games.Where(g => g.AppId == appId)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.CreatedAt, Now.AddDays(-days)));
        }

        await using var read = new CatoDbContext(_options);
        var candidates = await FollowerWatchQueue.LoadCandidatesAsync(read, 90, Now);

        Assert.Equal([1, 3, 9], candidates.Daily.Order());
        Assert.Equal([2], candidates.Weekly.Order());
    }

    private static Game Game(int appId, string type, DateTime createdAt, bool filtered = false, bool enriched = true) => new()
    {
        Id = Guid.NewGuid(), AppId = appId, Name = $"Game {appId}", GameType = type, CreatedAt = createdAt,
        IsFiltered = filtered, LastEnrichedAt = enriched ? createdAt : null,
    };
}
