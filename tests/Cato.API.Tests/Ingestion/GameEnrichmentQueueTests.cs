using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers <see cref="GameEnrichmentQueue"/> — which games the backfill sweep picks,
/// and in what order. The ordering is the whole point of the service: reddit-analysed
/// games sit behind tens of thousands of discovered ones and would never be reached
/// on app-id order alone.
/// </summary>
public class GameEnrichmentQueueTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public GameEnrichmentQueueTests()
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

    private static readonly DateTime Now = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    private async Task<Guid> SeedAsync(
        int appId,
        DateTime? analyzedAt = null,
        DateTime? lastEnrichedAt = null,
        int failures = 0)
    {
        var game = new Game
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            Name = $"Game {appId}",
            GameType = "Other",
            AnalyzedAt = analyzedAt,
            LastEnrichedAt = lastEnrichedAt,
            EnrichmentFailures = failures
        };

        await using var db = NewContext();
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return game.Id;
    }

    private async Task<GameEnrichmentBatch> SelectAsync(GameEnrichmentSettings settings)
    {
        await using var db = NewContext();
        return await GameEnrichmentQueue.SelectAsync(db, settings);
    }

    [Fact]
    public async Task Analysed_games_come_first_newest_analysis_leading()
    {
        var old = await SeedAsync(100, analyzedAt: Now.AddDays(-5));
        var recent = await SeedAsync(200, analyzedAt: Now.AddDays(-1));
        var discovered = await SeedAsync(1, analyzedAt: null);

        var batch = await SelectAsync(new GameEnrichmentSettings { BatchSize = 10 });

        Assert.Equal([recent, old, discovered], batch.GameIds);
        Assert.Equal(2, batch.Analysed);
    }

    [Fact]
    public async Task A_full_batch_of_analysed_games_leaves_no_room_for_the_rest()
    {
        await SeedAsync(100, analyzedAt: Now.AddDays(-1));
        await SeedAsync(200, analyzedAt: Now.AddDays(-2));
        var discovered = await SeedAsync(1);

        var batch = await SelectAsync(new GameEnrichmentSettings { BatchSize = 2 });

        Assert.Equal(2, batch.GameIds.Count);
        Assert.DoesNotContain(discovered, batch.GameIds);
    }

    [Fact]
    public async Task Enriched_games_are_left_alone_when_no_refresh_window_is_set()
    {
        await SeedAsync(100, analyzedAt: Now.AddDays(-1), lastEnrichedAt: Now.AddYears(-1));

        var batch = await SelectAsync(new GameEnrichmentSettings { BatchSize = 10 });

        Assert.Empty(batch.GameIds);
    }

    [Fact]
    public async Task A_refresh_window_picks_up_the_stalest_records_last()
    {
        var never = await SeedAsync(100, analyzedAt: Now.AddDays(-1));
        var stalest = await SeedAsync(200, lastEnrichedAt: Now.AddDays(-90));
        var stale = await SeedAsync(300, lastEnrichedAt: Now.AddDays(-40));
        var fresh = await SeedAsync(400, lastEnrichedAt: DateTime.UtcNow.AddDays(-1));

        var batch = await SelectAsync(new GameEnrichmentSettings { BatchSize = 10, RefreshAfterDays = 30 });

        Assert.Equal([never, stalest, stale], batch.GameIds);
        Assert.DoesNotContain(fresh, batch.GameIds);
        Assert.Equal(2, batch.Refresh);
    }

    [Fact]
    public async Task A_stale_reddit_game_outranks_a_never_enriched_discovered_one()
    {
        // The reason the bands exist. On "never enriched first" ordering, the stale
        // reddit game would sit behind every discovered game in the catalogue.
        var staleReddit = await SeedAsync(
            900, analyzedAt: Now.AddDays(-1), lastEnrichedAt: Now.AddDays(-90));
        var neverDiscovered = await SeedAsync(1);

        var batch = await SelectAsync(
            new GameEnrichmentSettings { BatchSize = 10, RefreshAfterDays = 30 });

        Assert.Equal([staleReddit, neverDiscovered], batch.GameIds);
    }

    [Fact]
    public async Task A_never_enriched_reddit_game_still_outranks_a_stale_one()
    {
        var staleReddit = await SeedAsync(
            900, analyzedAt: Now.AddDays(-1), lastEnrichedAt: Now.AddDays(-90));
        var neverReddit = await SeedAsync(901, analyzedAt: Now.AddDays(-5));

        var batch = await SelectAsync(
            new GameEnrichmentSettings { BatchSize = 10, RefreshAfterDays = 30 });

        Assert.Equal([neverReddit, staleReddit], batch.GameIds);
        Assert.Equal(2, batch.Analysed);
        Assert.Equal(1, batch.Refresh);
    }

    [Fact]
    public async Task Reddit_games_can_be_refreshed_while_the_rest_are_left_alone()
    {
        var staleReddit = await SeedAsync(
            900, analyzedAt: Now.AddDays(-1), lastEnrichedAt: Now.AddDays(-90));
        await SeedAsync(1, lastEnrichedAt: Now.AddDays(-90));
        await SeedAsync(2);

        var batch = await SelectAsync(new GameEnrichmentSettings
        {
            BatchSize = 10,
            RefreshAfterDays = 30,
            IncludeUnanalyzedGames = false
        });

        Assert.Equal([staleReddit], batch.GameIds);
    }

    [Fact]
    public async Task Games_at_the_failure_threshold_are_dropped()
    {
        await SeedAsync(100, analyzedAt: Now.AddDays(-1), failures: 5);
        var retryable = await SeedAsync(200, analyzedAt: Now.AddDays(-2), failures: 4);

        var batch = await SelectAsync(new GameEnrichmentSettings { BatchSize = 10, FailureThreshold = 5 });

        Assert.Equal([retryable], batch.GameIds);
    }

    [Fact]
    public async Task Discovered_games_can_be_excluded_entirely()
    {
        var analysed = await SeedAsync(100, analyzedAt: Now.AddDays(-1));
        await SeedAsync(1);

        var batch = await SelectAsync(
            new GameEnrichmentSettings { BatchSize = 10, IncludeUnanalyzedGames = false });

        Assert.Equal([analysed], batch.GameIds);
    }

    [Theory]
    // What the store serves for a released game.
    [InlineData("Oct 14, 2026", "2026-10-14")]
    [InlineData("14 Oct, 2026", "2026-10-14")]
    [InlineData("October 14, 2026", "2026-10-14")]
    [InlineData("Oct 2026", "2026-10-01")]
    [InlineData("October 2026", "2026-10-01")]
    // What it serves for an unreleased one — no date exists, so none is invented.
    [InlineData("Q4 2026", null)]
    [InlineData("2026", null)]
    [InlineData("To be announced", null)]
    [InlineData("Coming soon", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Steam_release_dates_parse_only_when_a_real_date_is_given(string? raw, string? expected)
    {
        var parsed = SteamGameEnrichmentService.TryParseSteamReleaseDate(raw, out var date);

        Assert.Equal(expected is not null, parsed);
        if (expected is not null)
            Assert.Equal(DateOnly.Parse(expected), date);
    }
}
