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
    private static readonly Guid ChefGameId = Guid.NewGuid();

    private static object Row(int appId, string? name, long impressions, long visits) =>
        new { app_id = appId, name, impressions, visits };

    private static JsonElement Item(string start, string end, string attribution, params object[] rows) =>
        PublisherItem(Publisher, start, end, attribution, rows);

    private static JsonElement PublisherItem(int publisher, string start, string end, string attribution, params object[] rows) =>
        JsonSerializer.SerializeToElement(new
        {
            publisher_id = publisher,
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

    private async Task<ItemIngestResult> IngestAsync(JsonElement item, int publisher = Publisher)
    {
        await using var db = NewContext();
        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestSteamworksNavStatsItemAsync(publisher, DateTimeOffset.UtcNow, item);
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
            seed.Games.Add(new Game { Id = ChefGameId, AppId = 2390230, Name = "The Chef's Shift", GameType = "Owned" });
            await seed.SaveChangesAsync();
        }
        await IngestAsync(Item("2026-09-01", "2026-09-30", "all", Row(2390230, "The Chef's Shift", 9_000_000, 150_000)));
        await IngestAsync(Item("2026-10-17", "2026-10-17", "all", Row(2390230, "The Chef's Shift", 700000, 12205), Row(2361460, "Toy Shire", 200000, 4807)));
        await IngestAsync(Item("2026-10-18", "2026-10-18", "all", Oct18All));
        await IngestAsync(Item("2026-10-18", "2026-10-18", "store", Row(2390230, "The Chef's Shift", 695533, 7170)));
        await IngestAsync(Item("2026-10-18", "2026-10-18", "external", Row(2390230, "The Chef's Shift", 30000, 2500)));

        await using var db = NewContext();
        var dto = await new GameDataService(db).GetNavStatsAsync(
            new GetNavStatsQuery(Publisher, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 18)));

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
        Assert.Equal(ChefGameId, chef.GameId);
        Assert.Equal((1_429_526L, 22_202L, 695_533L, 7_170L, 30_000L, 2_500L),
            (chef.Impressions, chef.Visits, chef.StoreImpressions, chef.StoreVisits, chef.ExternalImpressions, chef.ExternalVisits));
        Assert.Null(dto.Products[2].GameId);
        Assert.Equal("Ominous Stew Playtest", dto.Products[2].Name);
    }

    [Fact]
    public async Task QueryShowsOnePublisherAndListsThemAll()
    {
        const int planzet = 337008, bekir = 356969;
        await using (var seed = NewContext())
        {
            seed.SteamworksPartners.Add(new SteamworksPartner { PartnerId = planzet, Name = "PLANZET - FZCO", Enabled = true, DiscoveredAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        await IngestAsync(Item("2026-11-09", "2026-11-09", "all", Row(2094910, "Pile Up!", 9000, 900)));
        await IngestAsync(PublisherItem(planzet, "2026-11-09", "2026-11-09", "all", Row(2094910, "Pile Up!", 2000, 250)), planzet);
        await IngestAsync(PublisherItem(bekir, "2026-11-10", "2026-11-10", "all", Row(4904560, "The Waking Grove", 100, 50)), bekir);

        await using var db = NewContext();
        var service = new GameDataService(db);
        var standard = await service.GetNavStatsAsync(new GetNavStatsQuery(Publisher, null, null));
        Assert.Equal(Publisher, standard.PublisherId);
        Assert.Equal([(2094910, 900L)], standard.Products.Select(p => (p.AppId, p.Visits)).ToList());
        Assert.Equal([Publisher, planzet, bekir], standard.Publishers.Select(p => p.PublisherId).ToList());
        Assert.Equal("PLANZET - FZCO", standard.Publishers[1].Name);
        Assert.Null(standard.Publishers[2].Name); // no registry row: the page shows the id

        // Publishers are never summed: the same product shows under both with its own numbers.
        var chosen = await service.GetNavStatsAsync(new GetNavStatsQuery(planzet, null, null));
        Assert.Equal(planzet, chosen.PublisherId);
        Assert.Equal([(2094910, 250L)], chosen.Products.Select(p => (p.AppId, p.Visits)).ToList());
    }

    [Fact]
    public async Task AllOrganizationsCountsEachProductOnceFromTheBetterCoveredPublisher()
    {
        const int planzet = 337008, bekir = 356969;
        // Pile Up! under PLAN B: daily Nov 8-10. Under PLANZET: a month row (Nov 1-7) and the same
        // three days with identical numbers. The month row is outside PLAN B's coverage.
        await IngestAsync(Item("2026-11-08", "2026-11-08", "all", Row(2094910, "Pile Up!", 800, 80), Row(111, "Only B", 10, 5)));
        await IngestAsync(Item("2026-11-09", "2026-11-09", "all", Row(2094910, "Pile Up!", 900, 90)));
        await IngestAsync(Item("2026-11-10", "2026-11-10", "all", Row(2094910, "Pile Up!", 1000, 100)));
        await IngestAsync(PublisherItem(planzet, "2026-11-01", "2026-11-07", "all", Row(2094910, "Pile Up!", 7000, 700)), planzet);
        await IngestAsync(PublisherItem(planzet, "2026-11-08", "2026-11-08", "all", Row(2094910, "Pile Up!", 800, 80), Row(222, "Only P", 20, 7)), planzet);
        await IngestAsync(PublisherItem(planzet, "2026-11-09", "2026-11-09", "all", Row(2094910, "Pile Up!", 900, 90)), planzet);
        await IngestAsync(PublisherItem(planzet, "2026-11-10", "2026-11-10", "all", Row(2094910, "Pile Up!", 1000, 100)), planzet);
        await IngestAsync(PublisherItem(planzet, "2026-11-10", "2026-11-10", "store", Row(2094910, "Pile Up!", 500, 40)), planzet);
        await IngestAsync(PublisherItem(bekir, "2026-11-10", "2026-11-10", "all", Row(4904560, "The Waking Grove", 100, 50)), bekir);

        await using var db = NewContext();
        var service = new GameDataService(db);
        var all = await service.GetNavStatsAsync(new GetNavStatsQuery(null, null, null));

        Assert.Null(all.PublisherId);
        Assert.Equal(3, all.Publishers.Count);
        // PLANZET covers Pile Up! better (970 vs 270 visits): counted once from PLANZET.
        var pileUp = all.Products.Single(p => p.AppId == 2094910);
        Assert.Equal((970L, 40L), (pileUp.Visits, pileUp.StoreVisits));
        Assert.Equal([2094910, 4904560, 222, 111], all.Products.Select(p => p.AppId).ToList());
        Assert.Equal((970L + 50 + 7 + 5, 40L), (all.Totals[0].Visits, all.Totals[1].Visits));
        Assert.Equal(
            [(new DateOnly(2026, 11, 1), 700L), (new DateOnly(2026, 11, 8), 92L), (new DateOnly(2026, 11, 9), 90L), (new DateOnly(2026, 11, 10), 150L)],
            all.Periods.Select(p => (p.PeriodStart, p.Visits)).ToList());
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 8)),
            (all.FirstDate!.Value, all.LastDate!.Value, all.FirstDailyDate!.Value));

        // 0 means all too; a specific id keeps its own rows (PLAN B has no month row).
        var zero = await service.GetNavStatsAsync(new GetNavStatsQuery(0, null, null));
        Assert.Equal(all.Totals[0].Visits, zero.Totals[0].Visits);
        var planB = await service.GetNavStatsAsync(new GetNavStatsQuery(Publisher, null, null));
        Assert.Equal(Publisher, planB.PublisherId);
        Assert.Equal(270L, planB.Products.Single(p => p.AppId == 2094910).Visits);
        Assert.DoesNotContain(planB.Products, p => p.AppId == 222);

        // The source does not depend on the range: PLANZET stays the source inside Nov 9-10 (its store row counts).
        var tie = await service.GetNavStatsAsync(new GetNavStatsQuery(null, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10)));
        Assert.Equal(190L, tie.Products.Single(p => p.AppId == 2094910).Visits);
        Assert.Equal(40L, tie.Products.Single(p => p.AppId == 2094910).StoreVisits);
        Assert.Equal(all.FirstDate, tie.FirstDate);
        Assert.Equal(all.FirstDailyDate, tie.FirstDailyDate);
    }

    [Fact]
    public async Task QueryWithoutDataIsEmpty()
    {
        await using var db = NewContext();
        var dto = await new GameDataService(db).GetNavStatsAsync(new GetNavStatsQuery(null, null, null));

        Assert.Null(dto.PublisherId);
        Assert.Empty(dto.Products);
        Assert.Empty(dto.Publishers);
    }
}
