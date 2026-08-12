using System.Text.Json;
using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers <see cref="IngestionService.IngestFollowerHistoryItemAsync"/>. Runs on
/// SQLite rather than the InMemory provider specifically so the unique index on
/// (GameId, SnapshotDate, Source) is actually enforced — several of these tests
/// are about not violating it.
/// </summary>
public class FollowerHistoryIngestionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public FollowerHistoryIngestionTests()
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

    private static IngestionService NewService(CatoDbContext db) =>
        new(db, NullLogger<IngestionService>.Instance, new StubGameService());

    private static readonly Guid GameId = Guid.NewGuid();
    private const int AppId = 2429270;
    private static readonly DateTimeOffset ScrapedAt =
        new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private async Task SeedGameAsync()
    {
        await using var db = NewContext();
        db.Games.Add(new Game
        {
            Id = GameId,
            AppId = AppId,
            Name = "The RPG",
            GameType = "Sourcing"
        });
        await db.SaveChangesAsync();
    }

    private static JsonElement Payload(params (string Date, object? Count)[] rows)
    {
        var items = rows.Select(r => r.Count is null
            ? $"{{\"date\":\"{r.Date}\"}}"
            : $"{{\"date\":\"{r.Date}\",\"follower_count\":{JsonSerializer.Serialize(r.Count)}}}");
        return JsonDocument.Parse($"{{\"rows\":[{string.Join(",", items)}]}}").RootElement;
    }

    private async Task<ItemIngestResult> IngestAsync(JsonElement data)
    {
        await using var db = NewContext();
        var result = await NewService(db).IngestFollowerHistoryItemAsync(AppId, ScrapedAt, data);
        await db.SaveChangesAsync();
        return result;
    }

    private async Task<List<GroupMemberCountSnapshot>> RowsAsync(string? source = null)
    {
        await using var db = NewContext();
        var q = db.GroupMemberCountSnapshots.Where(s => s.GameId == GameId);
        if (source is not null) q = q.Where(s => s.Source == source);
        return await q.OrderBy(s => s.SnapshotDate).ToListAsync();
    }

    [Fact]
    public async Task InsertsOneRowPerDatedEntry()
    {
        await SeedGameAsync();

        var result = await IngestAsync(Payload(
            ("2024-03-11", 6120),
            ("2024-03-12", 6180),
            ("2024-03-13", 6205)));

        Assert.Equal(new ItemIngestResult(3, 3, 0, 0), result);

        var rows = await RowsAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal([6120, 6180, 6205], rows.Select(r => r.MemberCount));
        Assert.All(rows, r =>
            Assert.Equal(GroupMemberCountSnapshot.SteamDbFollowerSource, r.Source));
    }

    [Fact]
    public async Task SnapshotDateComesFromTheRowNotTheScrapeTime()
    {
        await SeedGameAsync();

        await IngestAsync(Payload(("2019-01-05", 42)));

        var row = Assert.Single(await RowsAsync());
        // The scrape happened in 2026; the row must be dated 2019.
        Assert.Equal(new DateOnly(2019, 1, 5), row.SnapshotDate);
        Assert.Equal(ScrapedAt.UtcDateTime, row.ScrapedAt);
    }

    [Fact]
    public async Task RerunUpdatesInPlaceInsteadOfDuplicating()
    {
        await SeedGameAsync();
        await IngestAsync(Payload(("2024-03-11", 6120), ("2024-03-12", 6180)));

        // Same dates with corrected values, plus one new day.
        var result = await IngestAsync(Payload(
            ("2024-03-11", 6121),
            ("2024-03-12", 6181),
            ("2024-03-13", 6205)));

        Assert.Equal(new ItemIngestResult(3, 1, 2, 0), result);

        var rows = await RowsAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal([6121, 6181, 6205], rows.Select(r => r.MemberCount));
    }

    [Fact]
    public async Task DuplicateDateWithinOnePayloadDoesNotTripTheUniqueIndex()
    {
        await SeedGameAsync();

        // Staging two inserts for the same (game, date, source) would throw on
        // commit — the last value must win instead.
        var result = await IngestAsync(Payload(
            ("2024-03-11", 100),
            ("2024-03-11", 200)));

        Assert.Equal(new ItemIngestResult(2, 1, 1, 0), result);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(200, row.MemberCount);
    }

    [Theory]
    [InlineData("not-a-date", 5)]
    [InlineData("2024-13-45", 5)]
    public async Task MalformedDateIsCountedFailedNotThrown(string date, int count)
    {
        await SeedGameAsync();

        var result = await IngestAsync(Payload((date, count), ("2024-03-12", 6180)));

        Assert.Equal(new ItemIngestResult(2, 1, 0, 1), result);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(6180, row.MemberCount);
    }

    [Fact]
    public async Task MissingOrNonNumericCountIsCountedFailedNotThrown()
    {
        await SeedGameAsync();

        var data = JsonDocument.Parse("""
            {"rows":[
              {"date":"2024-03-11"},
              {"date":"2024-03-12","follower_count":null},
              {"date":"2024-03-13","follower_count":"lots"},
              {"date":"2024-03-14","follower_count":6205}
            ]}
            """).RootElement;

        var result = await IngestAsync(data);

        Assert.Equal(new ItemIngestResult(4, 1, 0, 3), result);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(6205, row.MemberCount);
    }

    [Fact]
    public async Task MissingRowsArrayIsReportedNotThrown()
    {
        await SeedGameAsync();

        var result = await IngestAsync(JsonDocument.Parse("""{"game_id":730}""").RootElement);

        Assert.Equal(new ItemIngestResult(0, 0, 0, 1), result);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task EmptyRowsArrayIsANoOp()
    {
        await SeedGameAsync();

        var result = await IngestAsync(JsonDocument.Parse("""{"rows":[]}""").RootElement);

        Assert.Equal(new ItemIngestResult(0, 0, 0, 0), result);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task BackfillAndDailyScrapeCoexistOnTheSameDate()
    {
        await SeedGameAsync();

        // The live daily scraper's row for a date the backfill also covers.
        await using (var db = NewContext())
        {
            db.GroupMemberCountSnapshots.Add(new GroupMemberCountSnapshot
            {
                Id = Guid.NewGuid(),
                GameId = GameId,
                SnapshotDate = new DateOnly(2024, 3, 11),
                Source = GroupMemberCountSnapshot.SteamCommunitySource,
                MemberCount = 9999,
                ScrapedAt = ScrapedAt.UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        var result = await IngestAsync(Payload(("2024-03-11", 6120)));

        // Inserted, not updated: the community-group row is a different series.
        Assert.Equal(new ItemIngestResult(1, 1, 0, 0), result);

        Assert.Equal(9999, Assert.Single(
            await RowsAsync(GroupMemberCountSnapshot.SteamCommunitySource)).MemberCount);
        Assert.Equal(6120, Assert.Single(
            await RowsAsync(GroupMemberCountSnapshot.SteamDbFollowerSource)).MemberCount);
    }

    [Fact]
    public async Task DailyScrapeDoesNotOverwriteABackfilledRow()
    {
        await SeedGameAsync();
        await IngestAsync(Payload(("2026-08-11", 6120)));   // same day as ScrapedAt

        await using (var db = NewContext())
        {
            var data = JsonDocument.Parse("""{"group_member_count":7000}""").RootElement;
            await NewService(db).IngestGroupMemberCountItemAsync(AppId, ScrapedAt, data);
            await db.SaveChangesAsync();
        }

        Assert.Equal(6120, Assert.Single(
            await RowsAsync(GroupMemberCountSnapshot.SteamDbFollowerSource)).MemberCount);
        Assert.Equal(7000, Assert.Single(
            await RowsAsync(GroupMemberCountSnapshot.SteamCommunitySource)).MemberCount);
    }

    /// <summary>
    /// Every follower test pre-seeds the game, so <c>FindOrStubGameAsync</c> returns
    /// on its first line and none of these members are ever reached.
    /// </summary>
    private sealed class StubGameService : IGameService
    {
        public Task<Result<GameDto>> CreateGameAsync(CreateGameCommand c, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Result<UpsertGameResult>> UpsertGameAsync(CreateGameCommand c, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PagedResult<GameDto>> ListGamesAsync(ListGamesQuery q, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<PagedResult<GameDto>> CatalogGamesAsync(CatalogGamesQuery q, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Result<GameDto>> GetGameDetailsAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Result<GameDto>> UpdateGameAsync(UpdateGameCommand c, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Result<bool>> DeleteGameAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Result<GameDto>> EnrichGameFromSteamAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
