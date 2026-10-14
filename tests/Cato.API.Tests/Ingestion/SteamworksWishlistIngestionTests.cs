using System.Text;
using System.Text.Json;
using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers <see cref="IngestionService.IngestSteamworksWishlistReportItemAsync"/> and the
/// manual CSV upload that shares its upsert. On SQLite so the unique indexes on
/// (GameId, DateLocal) and (GameId, DateLocal, MonthCohort) are enforced: every run
/// re-sends the full history, and "update, never duplicate" is the requirement.
/// </summary>
public class SteamworksWishlistIngestionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public SteamworksWishlistIngestionTests()
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
        new(db, NullLogger<IngestionService>.Instance);

    private static readonly Guid GameId = Guid.NewGuid();
    private const int AppId = 2390230;
    private static readonly DateTimeOffset ScrapedAt =
        new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    private async Task SeedGameAsync()
    {
        await using var db = NewContext();
        db.Games.Add(new Game
        {
            Id = GameId,
            AppId = AppId,
            Name = "The Chef's Shift",
            GameType = "Owned"
        });
        await db.SaveChangesAsync();
    }

    private static object Day(string date, int adds, int deletes = 0, int purchases = 0, int gifts = 0) => new
    {
        date_local = date,
        adds,
        deletes,
        purchases_and_activations = purchases,
        gifts
    };

    private static object Cohort(string date, string month, int purchases, int gifts = 0) => new
    {
        date_local = date,
        month_cohort = month,
        purchases_and_activations = purchases,
        gifts,
        total_conversions = purchases + gifts
    };

    private static JsonElement Payload(object[]? actions, object[]? cohorts, string? name = "The Chef's Shift") =>
        JsonSerializer.SerializeToElement(new { game_name = name, actions, cohorts });

    private async Task<ItemIngestResult> IngestAsync(JsonElement data, int appId = AppId)
    {
        await using var db = NewContext();
        var result = await NewService(db).IngestSteamworksWishlistReportItemAsync(appId, ScrapedAt, data);
        await db.SaveChangesAsync();
        return result;
    }

    private async Task<List<SteamworksWishlistDaily>> DaysAsync()
    {
        await using var db = NewContext();
        return await db.SteamworksWishlistDaily.Where(d => d.GameId == GameId).OrderBy(d => d.DateLocal).ToListAsync();
    }

    private async Task<List<SteamworksWishlistCohort>> CohortsAsync()
    {
        await using var db = NewContext();
        return await db.SteamworksWishlistCohorts.Where(c => c.GameId == GameId)
            .OrderBy(c => c.DateLocal).ThenBy(c => c.MonthCohort).ToListAsync();
    }

    [Fact]
    public async Task InsertsBothSeries()
    {
        await SeedGameAsync();

        var result = await IngestAsync(Payload(
            [Day("2023-04-10", 1), Day("2023-04-11", 0, purchases: 1)],
            [Cohort("2023-04-11", "2023-04-01", 1), Cohort("2026-01-01", "2025-12-01", 36, 2)]));

        Assert.Equal(new ItemIngestResult(4, 4, 0, 0), result);

        var days = await DaysAsync();
        Assert.Equal([new DateOnly(2023, 4, 10), new DateOnly(2023, 4, 11)], days.Select(d => d.DateLocal));
        Assert.Equal(1, days[1].PurchasesAndActivations);

        var cohorts = await CohortsAsync();
        Assert.Equal(2, cohorts.Count);
        Assert.Equal(new DateOnly(2025, 12, 1), cohorts[1].MonthCohort);
        Assert.Equal(38, cohorts[1].TotalConversions);
    }

    [Fact]
    public async Task ReExportUpdatesChangedRowsInPlace()
    {
        await SeedGameAsync();
        await IngestAsync(Payload(
            [Day("2026-01-29", 254, 31, 32, 1), Day("2026-01-30", 266, 35, 39, 2)],
            [Cohort("2026-01-01", "2025-12-01", 36, 2)]));

        // A later export: one day revised, one day unchanged, one new day; the
        // cohort row revised.
        var result = await IngestAsync(Payload(
            [Day("2026-01-29", 254, 31, 32, 1), Day("2026-01-30", 270, 35, 40, 2), Day("2026-01-31", 12)],
            [Cohort("2026-01-01", "2025-12-01", 37, 2)]));

        Assert.Equal(new ItemIngestResult(4, 1, 2, 0), result);

        var days = await DaysAsync();
        Assert.Equal(3, days.Count);
        Assert.Equal(270, days[1].Adds);
        Assert.Equal(40, days[1].PurchasesAndActivations);

        var cohort = Assert.Single(await CohortsAsync());
        Assert.Equal(37, cohort.PurchasesAndActivations);
        Assert.Equal(39, cohort.TotalConversions);
    }

    [Fact]
    public async Task IdenticalReExportWritesNothing()
    {
        await SeedGameAsync();
        var payload = Payload([Day("2026-01-30", 266, 35, 39, 2)], [Cohort("2026-01-01", "2025-12-01", 36, 2)]);
        await IngestAsync(payload);
        var before = Assert.Single(await DaysAsync()).UpdatedAt;

        var result = await IngestAsync(payload);

        Assert.Equal(new ItemIngestResult(2, 0, 0, 0), result);
        Assert.Equal(before, Assert.Single(await DaysAsync()).UpdatedAt);
    }

    [Fact]
    public async Task RowsMissingFromALaterExportAreKept()
    {
        await SeedGameAsync();
        await IngestAsync(Payload([Day("2026-01-29", 5), Day("2026-01-30", 6)], null));

        await IngestAsync(Payload([Day("2026-01-30", 6)], null));

        Assert.Equal(2, (await DaysAsync()).Count);
    }

    [Fact]
    public async Task MissingCohortsLeaveTheCohortTableUntouched()
    {
        await SeedGameAsync();
        await IngestAsync(Payload([Day("2026-01-30", 6)], [Cohort("2026-01-01", "2025-12-01", 36)]));

        // The collector sends cohorts = null when only the cohort download failed.
        var result = await IngestAsync(Payload([Day("2026-01-30", 7)], null));

        Assert.Equal(new ItemIngestResult(1, 0, 1, 0), result);
        Assert.Single(await CohortsAsync());
    }

    [Fact]
    public async Task MalformedRowsAreSkippedNotThrown()
    {
        await SeedGameAsync();
        var data = JsonDocument.Parse("""
            {
              "actions": [
                {"date_local": "2026-01-30", "adds": 6, "deletes": 0, "purchases_and_activations": 0, "gifts": 0},
                {"date_local": "30/01/2026", "adds": 6, "deletes": 0, "purchases_and_activations": 0, "gifts": 0},
                {"date_local": "2026-01-31", "adds": "6", "deletes": 0, "purchases_and_activations": 0, "gifts": 0},
                null
              ],
              "cohorts": [
                {"date_local": "2026-01-01", "month_cohort": "2025-12-01", "purchases_and_activations": 1, "gifts": 0}
              ]
            }
            """).RootElement;

        var result = await IngestAsync(data);

        Assert.Equal(new ItemIngestResult(5, 1, 0, 4), result);
        Assert.Single(await DaysAsync());
        Assert.Empty(await CohortsAsync());
    }

    [Fact]
    public async Task DuplicateKeysWithinOnePayloadDoNotTripTheUniqueIndex()
    {
        await SeedGameAsync();

        var result = await IngestAsync(Payload(
            [Day("2026-01-30", 6), Day("2026-01-30", 9)],
            [Cohort("2026-01-01", "2025-12-01", 1), Cohort("2026-01-01", "2025-12-01", 3)]));

        Assert.Equal(new ItemIngestResult(4, 2, 2, 0), result);
        Assert.Equal(9, Assert.Single(await DaysAsync()).Adds);
        Assert.Equal(3, Assert.Single(await CohortsAsync()).PurchasesAndActivations);
    }

    [Fact]
    public async Task DuplicateKeyOfAnExistingRowWithinOnePayloadUpdatesOnce()
    {
        await SeedGameAsync();
        await IngestAsync(Payload([Day("2026-01-30", 6)], null));

        await IngestAsync(Payload([Day("2026-01-30", 7), Day("2026-01-30", 8)], null));

        Assert.Equal(8, Assert.Single(await DaysAsync()).Adds);
    }

    [Fact]
    public async Task UnknownAppIsStubbedWithTheReportName()
    {
        var result = await IngestAsync(Payload([Day("2026-01-30", 6)], null, "New Game"), appId: 9999990);

        Assert.Equal(1, result.Inserted);
        await using var db = NewContext();
        var game = await db.Games.SingleAsync(g => g.AppId == 9999990);
        Assert.Equal("New Game", game.Name);
        Assert.Equal("Other", game.GameType);
    }

    [Fact]
    public async Task ManualUploadParsesThePartnerExportAndUpserts()
    {
        await SeedGameAsync();
        const string csv = """
            sep=,
            Steam Wishlisting data for 2000-01-01 - 2026-01-30

            DateLocal,Game,Adds,Deletes,PurchasesAndActivations,Gifts
            2026-01-29,The Chef's Shift,254,31,32,1
            2026-01-30,"Chef, The",266,35,39,2
            not-a-date,The Chef's Shift,1,1,1,1
            """;

        IngestionResult first;
        await using (var db = NewContext())
            first = await NewService(db).IngestWishlistDataAsync(
                new IngestWishlistDataCommand(AppId, "wishlist.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv))));

        Assert.Equal(3, first.RecordsProcessed);
        Assert.Equal(2, first.RecordsInserted);
        Assert.Equal(1, first.RecordsFailed);

        var revised = csv.Replace("2026-01-30,\"Chef, The\",266", "2026-01-30,\"Chef, The\",300");
        IngestionResult second;
        await using (var db = NewContext())
            second = await NewService(db).IngestWishlistDataAsync(
                new IngestWishlistDataCommand(AppId, "wishlist.csv", new MemoryStream(Encoding.UTF8.GetBytes(revised))));

        Assert.Equal(0, second.RecordsInserted);
        Assert.Equal(1, second.RecordsUpdated);

        var days = await DaysAsync();
        Assert.Equal(2, days.Count);
        Assert.Equal(300, days[1].Adds);
        Assert.Equal(2, days[1].Gifts);
    }
}
