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
/// Covers the Steamworks publisher Nav Stats pipeline: the <c>steamworks_nav_stats</c>
/// item handler (one CSV = every product of a publisher for one period, source and
/// region) and the range query. Figures are from the real export of 2026-10-18.
/// </summary>
public class NavStatsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public NavStatsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private const int Publisher = 216530;

    private static object Row(int appId, string? name, long impressions, long visits) =>
        new { app_id = appId, name, impressions, visits };

    private static JsonElement Item(string start, string end, string attribution, params object[] rows) =>
        JsonSerializer.SerializeToElement(new
        {
            publisher_id = Publisher,
            period_start = start,
            period_end = end,
            attribution,
            region = "All Regions",
            rows,
        });

    private static readonly object[] Oct18All =
    [
        Row(2390230, "The Chef's Shift", 729526, 9997),
        Row(2361460, "Toy Shire", 188727, 4281),
        Row(3028820, "Ominous Stew Playtest", 1, 0),
    ];

    private async Task<ItemIngestResult> IngestAsync(JsonElement item)
    {
        await using var db = NewContext();
        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestSteamworksNavStatsItemAsync(Publisher, DateTimeOffset.UtcNow, item);
        await db.SaveChangesAsync();
        return result;
    }

    [Fact]
    public async Task StoresOneExportAndUpdatesItInPlace()
    {
        var first = await IngestAsync(Item("2026-10-18", "2026-10-18", "all", Oct18All));
        Assert.Equal((3, 3, 0, 0), (first.Processed, first.Inserted, first.Updated, first.Failed));

        var again = await IngestAsync(Item("2026-10-18", "2026-10-18", "all",
            Row(2390230, "The Chef's Shift", 730000, 10001),
            Row(2361460, "Toy Shire", 188727, 4281)));

        Assert.Equal((0, 1), (again.Inserted, again.Updated));
        await using var db = NewContext();
        var rows = await db.SteamworksNavStats.ToListAsync();
        Assert.Equal(3, rows.Count); // the playtest missing from the re-fetch keeps its row
        var chef = rows.Single(r => r.AppId == 2390230);
        Assert.Equal((730000L, 10001L, "The Chef's Shift"), (chef.Impressions, chef.Visits, chef.AppName));
        Assert.All(rows, r => Assert.Equal((Publisher, "all", "All Regions"), (r.PublisherId, r.Attribution, r.Region)));
        Assert.All(rows, r => Assert.NotEqual(default, r.UpdatedAt));
    }

    [Fact]
    public async Task SourcesAndPeriodsAreSeparateKeys()
    {
        await IngestAsync(Item("2026-10-18", "2026-10-18", "all", Oct18All));
        var store = await IngestAsync(Item("2026-10-18", "2026-10-18", "store", Row(2390230, "The Chef's Shift", 695533, 7170)));
        var month = await IngestAsync(Item("2026-09-01", "2026-09-30", "all", Row(2390230, "The Chef's Shift", 9_000_000, 150_000)));

        Assert.Equal((1, 1), (store.Inserted, month.Inserted));
        await using var db = NewContext();
        Assert.Equal(5, await db.SteamworksNavStats.CountAsync());
    }

    [Fact]
    public async Task RejectsAnUnknownSourceAndBadRows()
    {
        var unknown = await IngestAsync(Item("2026-10-18", "2026-10-18", "bots", Oct18All));
        Assert.Equal((3, 0, 3), (unknown.Processed, unknown.Inserted, unknown.Failed));

        var mixed = await IngestAsync(Item("2026-10-18", "2026-10-18", "all",
            Row(2390230, null, 5, 1),
            new { app_id = 0, impressions = 1, visits = 1 },
            new { app_id = 7, impressions = "many", visits = 1 }));
        Assert.Equal((3, 1, 2), (mixed.Processed, mixed.Inserted, mixed.Failed));
        await using var db = NewContext();
        Assert.Null((await db.SteamworksNavStats.SingleAsync()).AppName);
    }

    [Fact]
    public async Task QuerySumsTheRangeBySourcePeriodAndProduct()
    {
        await using (var seed = NewContext())
        {
            seed.Games.Add(new Game { Id = Guid.NewGuid(), AppId = 2390230, Name = "The Chef's Shift", GameType = "Owned" });
            await seed.SaveChangesAsync();
        }
        await IngestAsync(Item("2026-09-01", "2026-09-30", "all", Row(2390230, "The Chef's Shift", 9_000_000, 150_000)));
        await IngestAsync(Item("2026-10-17", "2026-10-17", "all", Row(2390230, "The Chef's Shift", 700000, 12205), Row(2361460, "Toy Shire", 200000, 4807)));
        await IngestAsync(Item("2026-10-18", "2026-10-18", "all", Oct18All));
        await IngestAsync(Item("2026-10-18", "2026-10-18", "store", Row(2390230, "The Chef's Shift", 695533, 7170)));
        await IngestAsync(Item("2026-10-18", "2026-10-18", "external", Row(2390230, "The Chef's Shift", 30000, 2500)));

        await using var db = NewContext();
        var dto = await new GameDataService(db).GetNavStatsAsync(
            new GetNavStatsQuery(null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 18)));

        Assert.Equal(Publisher, dto.PublisherId);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 17)),
            (dto.FirstDate!.Value, dto.LastDate!.Value, dto.FirstDailyDate!.Value));
        // The September month lies outside the range.
        Assert.Equal(
            [(new DateOnly(2026, 10, 17), 900000L, 17012L), (new DateOnly(2026, 10, 18), 918254L, 14278L)],
            dto.Periods.Select(p => (p.PeriodStart, p.Impressions, p.Visits)).ToList());
        Assert.Equal(["all", "store", "steamworks", "external"], dto.Totals.Select(t => t.Attribution).ToList());
        Assert.Equal((1_818_254L, 31_290L), (dto.Totals[0].Impressions, dto.Totals[0].Visits));
        Assert.Equal((0L, 0L), (dto.Totals[2].Impressions, dto.Totals[2].Visits));

        Assert.Equal([2390230, 2361460, 3028820], dto.Products.Select(p => p.AppId).ToList());
        var chef = dto.Products[0];
        Assert.True(chef.InCato);
        Assert.Equal((1_429_526L, 22_202L, 695_533L, 7_170L, 30_000L, 2_500L),
            (chef.Impressions, chef.Visits, chef.StoreImpressions, chef.StoreVisits, chef.ExternalImpressions, chef.ExternalVisits));
        Assert.False(dto.Products[2].InCato);
        Assert.Equal("Ominous Stew Playtest", dto.Products[2].Name);
    }

    [Fact]
    public async Task QueryWithoutDataIsEmpty()
    {
        await using var db = NewContext();
        var dto = await new GameDataService(db).GetNavStatsAsync(new GetNavStatsQuery(null, null, null));

        Assert.Null(dto.PublisherId);
        Assert.Empty(dto.Products);
    }
}
