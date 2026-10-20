using System.Text;
using System.Text.Json;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers the <c>steamworks_demo_downloads</c> item (one demo CSV per period, filed under
/// the main game), the manual upload sharing the table, and the range query. Figures
/// are from the demo 2432200 of 2390230 on 2026-10-18.
/// </summary>
public class DemoDownloadsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public DemoDownloadsTests()
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
    private const int DemoAppId = 2432200;

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        db.Games.Add(new Game { Id = GameId, AppId = AppId, Name = "The Chef's Shift", GameType = "Owned" });
        await db.SaveChangesAsync();
    }

    private static object Row(string geoType, string name, long downloads, double? share = null) =>
        new { geo_type = geoType, name, downloads, share_percent = share };

    private static JsonElement Item(string start, string end, params object[] rows) =>
        JsonSerializer.SerializeToElement(new { game_name = "The Chef's Shift", demo_app_id = DemoAppId, period_start = start, period_end = end, rows });

    private async Task<ItemIngestResult> IngestAsync(JsonElement item)
    {
        await using var db = NewContext();
        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestSteamworksDemoDownloadsItemAsync(AppId, DateTimeOffset.UtcNow, item);
        await db.SaveChangesAsync();
        return result;
    }

    [Fact]
    public async Task StoresAPeriodAndUpdatesItInPlace()
    {
        await SeedAsync();
        var first = await IngestAsync(Item("2026-10-18", "2026-10-18",
            Row("Region", "Asia", 201, 77.9), Row("Region", "North America", 35, 13.5), Row("Country", "China", 184, 71.3)));
        var again = await IngestAsync(Item("2026-10-18", "2026-10-18", Row("Region", "Asia", 205, 78.0)));
        var same = await IngestAsync(Item("2026-10-18", "2026-10-18", Row("Region", "Asia", 205, 78.0)));

        Assert.Equal((3, 0), (first.Inserted, first.Updated));
        Assert.Equal((0, 1), (again.Inserted, again.Updated));
        Assert.Equal((0, 0), (same.Inserted, same.Updated));
        await using var db = NewContext();
        var rows = await db.DemoDownloads.ToListAsync();
        Assert.Equal(3, rows.Count); // North America and China are kept
        Assert.All(rows, r => Assert.Equal((DemoAppId, new DateOnly(2026, 10, 18), GameId), (r.DemoAppId!.Value, r.PeriodEnd, r.GameId)));
        Assert.Equal((205L, 78.0m), (rows.Single(r => r.GeoName == "Asia").TotalDownloads, rows.Single(r => r.GeoName == "Asia").SharePercent!.Value));
    }

    [Fact]
    public async Task BadRowsAndPeriodsAreSkipped()
    {
        await SeedAsync();
        var bad = await IngestAsync(Item("2026-10-18", "2026-10-18",
            Row("Region", "Asia", 201), Row("Continent", "Asia", 1), Row("Country", "", 3), Row("Country", "China", -1)));
        var noPeriod = await IngestAsync(Item("2026-10-18", "2026-10-17", Row("Region", "Asia", 1)));

        Assert.Equal((4, 1, 3), (bad.Processed, bad.Inserted, bad.Failed));
        Assert.Equal((1, 0, 1), (noPeriod.Processed, noPeriod.Inserted, noPeriod.Failed));
    }

    [Fact]
    public async Task ManualUploadStillFilesUnderTheUploadDay()
    {
        await SeedAsync();
        var csv = "Region,Total Downloads,Share\n\"Asia\",90768,65.4%\nCountry,0,\n\"China\",82558,59.5%\n";
        await using (var db = NewContext())
        {
            var result = await new IngestionService(db, NullLogger<IngestionService>.Instance).IngestDemoDownloadsAsync(
                new IngestDemoDownloadsCommand(AppId, DemoAppId, "demo.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv))));
            Assert.Equal(2, result.RecordsInserted);
        }

        await using var check = NewContext();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.All(await check.DemoDownloads.ToListAsync(), r => Assert.Equal((today, today), (r.SnapshotDate, r.PeriodEnd)));
    }

    [Fact]
    public async Task QuerySumsThePeriodsInsideTheRange()
    {
        await SeedAsync();
        await IngestAsync(Item("2026-09-01", "2026-09-30", Row("Region", "Asia", 7000), Row("Country", "China", 6500)));
        await IngestAsync(Item("2026-10-17", "2026-10-17", Row("Region", "Asia", 190), Row("Region", "Africa", 0), Row("Country", "China", 170)));
        await IngestAsync(Item("2026-10-18", "2026-10-18", Row("Region", "Asia", 201), Row("Region", "North America", 35), Row("Country", "China", 184), Row("Country", "United States", 28)));

        await using var db = NewContext();
        var dto = await new GameDataService(db).GetDemoDownloadsAsync(new GetDemoDownloadsQuery(AppId, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 18)));

        Assert.Equal(DemoAppId, dto.DemoAppId);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 17)),
            (dto.FirstDate!.Value, dto.LastDate!.Value, dto.FirstDailyDate!.Value));
        Assert.Equal(426L, dto.Total); // the September month lies outside the range
        Assert.Equal([("Asia", 391L), ("North America", 35L)], dto.Regions.Select(r => (r.Name, r.Downloads)).ToList()); // Africa (0) left out
        Assert.Equal([("China", 354L), ("United States", 28L)], dto.Countries.Select(c => (c.Name, c.Downloads)).ToList());

        var allTime = await new GameDataService(db).GetDemoDownloadsAsync(new GetDemoDownloadsQuery(AppId, null, null));
        Assert.Equal(7426L, allTime.Total);
    }
}
