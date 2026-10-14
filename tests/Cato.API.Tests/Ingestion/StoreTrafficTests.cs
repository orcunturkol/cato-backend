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
/// Covers the Steamworks store traffic pipeline: the <c>steamworks_store_traffic</c>
/// item handler, the manual CSV upload sharing its upsert, and the period/breakdown
/// queries. Measured on the real export (2390230, 2026-10-12): non-Country rows sum to
/// the page totals, Bot Traffic has visits only, and Country rows split the same totals.
/// </summary>
public class StoreTrafficTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public StoreTrafficTests()
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

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        db.Games.Add(new Game { Id = GameId, AppId = AppId, Name = "The Chef's Shift", GameType = "Owned" });
        db.SteamCountries.Add(new SteamCountry { CountryCode = "CN", CountryName = "China", Region = "Asia" });
        await db.SaveChangesAsync();
    }

    private static object Row(string category, string feature, long impressions, long visits, long ownerImpressions = 0, long ownerVisits = 0) => new
    {
        category,
        feature,
        impressions,
        visits,
        owner_impressions = ownerImpressions,
        owner_visits = ownerVisits,
    };

    private static JsonElement Item(string start, string end, params object[] rows) =>
        JsonSerializer.SerializeToElement(new { game_name = "The Chef's Shift", period_start = start, period_end = end, rows });

    private static readonly object[] Oct12 =
    [
        Row("Tag Page", "Popular Titles", 37836, 588, 13, 0),
        Row("Home Page", "Main Capsule", 4411, 49),
        Row("Bot Traffic", "Crawler", 0, 191),
        Row("Country", "CN", 50000, 2000),
        Row("Country", "US", 12517, 334),
    ];

    private async Task<ItemIngestResult> IngestAsync(JsonElement item)
    {
        await using var db = NewContext();
        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestSteamworksStoreTrafficItemAsync(AppId, DateTimeOffset.UtcNow, item);
        await db.SaveChangesAsync();
        return result;
    }

    [Fact]
    public async Task StoresOnePeriodAndUpdatesItInPlace()
    {
        await SeedAsync();
        var first = await IngestAsync(Item("2026-10-12", "2026-10-12", Oct12));
        Assert.Equal(5, first.Inserted);

        var again = await IngestAsync(Item("2026-10-12", "2026-10-12",
            Row("Tag Page", "Popular Titles", 40000, 600, 13, 0)));

        Assert.Equal((0, 1), (again.Inserted, again.Updated));
        await using var db = NewContext();
        var rows = await db.SteamTrafficBreakdowns.ToListAsync();
        Assert.Equal(5, rows.Count);
        var tag = rows.Single(r => r.PageFeature == "Popular Titles");
        Assert.Equal((40000L, 600L, 13L), (tag.Impressions, tag.Visits, tag.OwnerImpressions));
        Assert.All(rows, r => Assert.Equal(new DateOnly(2026, 10, 12), r.PeriodEnd));
        Assert.All(rows, r => Assert.NotEqual(default, r.UpdatedAt));
    }

    [Fact]
    public async Task IdenticalReFetchWritesNothing()
    {
        await SeedAsync();
        await IngestAsync(Item("2026-10-12", "2026-10-12", Oct12));

        var again = await IngestAsync(Item("2026-10-12", "2026-10-12", Oct12));

        Assert.Equal((0, 0), (again.Inserted, again.Updated));
    }

    [Fact]
    public async Task RepeatedRowsInOnePayloadAreSummed()
    {
        await SeedAsync();

        await IngestAsync(Item("2026-10-12", "2026-10-12",
            Row("Tag Page", "(Other)", 100, 2),
            Row("Tag Page", "(Other)", 50, 1)));

        await using var db = NewContext();
        var row = await db.SteamTrafficBreakdowns.SingleAsync();
        Assert.Equal((150L, 3L), (row.Impressions, row.Visits));
    }

    [Fact]
    public async Task ItemWithoutAValidPeriodIsCountedAsFailed()
    {
        await SeedAsync();

        var result = await IngestAsync(Item("2026-10-12", "2026-10-11", Oct12));

        Assert.Equal((5, 5, 0), (result.Processed, result.Failed, result.Inserted));
    }

    [Fact]
    public async Task PeriodTotalsMatchThePageHeadline()
    {
        await SeedAsync();
        await IngestAsync(Item("2026-09-01", "2026-09-30", Row("Tag Page", "Popular Titles", 900000, 9000)));
        await IngestAsync(Item("2026-10-12", "2026-10-12", Oct12));

        await using var db = NewContext();
        var periods = await new GameDataService(db).GetStoreTrafficPeriodsAsync(
            new GetStoreTrafficPeriodsQuery(AppId, null, null));

        Assert.Equal(2, periods.Count);
        var month = periods[0];
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (month.PeriodStart, month.PeriodEnd));
        var day = periods[1];
        // Country rows are a second split, not more traffic; bots only add visits.
        Assert.Equal(42247L, day.Impressions);
        Assert.Equal(637L, day.Visits);
        Assert.Equal(191L, day.BotVisits);
        Assert.Equal(13L, day.OwnerImpressions);
    }

    [Fact]
    public async Task BreakdownSplitsSourcesAndCountriesInsideTheRange()
    {
        await SeedAsync();
        await IngestAsync(Item("2026-09-01", "2026-09-30", Row("Tag Page", "Popular Titles", 900000, 9000), Row("Country", "CN", 900000, 9000)));
        await IngestAsync(Item("2026-10-12", "2026-10-12", Oct12));
        await IngestAsync(Item("2026-10-13", "2026-10-13", Row("Tag Page", "Popular Titles", 1000, 12), Row("Country", "CN", 1000, 12)));

        await using var db = NewContext();
        // The range cuts September, so that month row stays out.
        var dto = await new GameDataService(db).GetStoreTrafficBreakdownAsync(
            new GetStoreTrafficBreakdownQuery(AppId, new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 13)));

        Assert.Equal(new DateOnly(2026, 9, 1), dto.FirstDate);
        Assert.Equal(new DateOnly(2026, 10, 13), dto.LastDate);
        Assert.Equal(new DateOnly(2026, 10, 12), dto.FirstDailyDate);
        Assert.Equal(["Popular Titles", "Crawler", "Main Capsule"], dto.Sources.Select(s => s.Feature));
        Assert.Equal(600L, dto.Sources[0].Visits);
        Assert.DoesNotContain(dto.Sources, s => s.Category == "Country");
        Assert.Equal(["CN", "US"], dto.Countries.Select(c => c.CountryCode));
        Assert.Equal(("China", "Asia", 2012L), (dto.Countries[0].CountryName, dto.Countries[0].Region, dto.Countries[0].Visits));
        Assert.Equal(("US", ""), (dto.Countries[1].CountryName, dto.Countries[1].Region));
    }

    [Fact]
    public async Task ManualUploadUsesTheSameUpsert()
    {
        await SeedAsync();
        const string csv = """
            "Page / Category","Page / Feature",Impressions,Visits,"Owner Impressions","Owner Visits"
            "Tag Page","Popular Titles","37,836",588,13,0
            "Tag Page","Popular Titles",100,2,0,0
            "Country",CN,50000,2000,0,0
            broken
            """;

        IngestionResult result;
        await using (var db = NewContext())
            result = await new IngestionService(db, NullLogger<IngestionService>.Instance).IngestStoreTrafficAsync(
                new IngestStoreTrafficCommand(AppId, "traffic.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv))));

        Assert.Equal((4, 2, 1), (result.RecordsProcessed, result.RecordsInserted, result.RecordsFailed));
        await using var check = NewContext();
        var tag = await check.SteamTrafficBreakdowns.SingleAsync(t => t.PageFeature == "Popular Titles");
        Assert.Equal((37936L, 590L, 13L), (tag.Impressions, tag.Visits, tag.OwnerImpressions));
        Assert.Equal(tag.SnapshotDate, tag.PeriodEnd);
    }
}
