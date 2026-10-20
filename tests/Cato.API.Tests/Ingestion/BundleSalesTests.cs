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
/// Covers the <c>steamworks_bundle_sales</c> item (a bundle's two Steamworks exports over a
/// window) and the bundle queries. Figures are from bundle 57829 (The RPG &amp; West of
/// Loathing, package 874080) on 2025-08-07/08.
/// </summary>
public class BundleSalesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public BundleSalesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private const int BundleId = 57829;
    private const int PackageId = 874080;

    private static object Sale(string date, int sold, int returned, double sales, double returns, double discount, double discountReturns) => new
    {
        date_local = date, partner_id = 216530, units_sold = sold, units_returned = returned,
        gross_sales_usd = sales, gross_returns_usd = returns, discount_usd = discount, discount_returns_usd = discountReturns,
    };

    private static object Package(string date, int sold, int returned, double sales, double returns) => new
    {
        date_local = date, package_id = PackageId, units_sold = sold, units_returned = returned,
        gross_sales_usd = sales, gross_returns_usd = returns, discount_usd = 0.0, discount_returns_usd = 0.0,
    };

    private static JsonElement Item(object[]? sales, object[]? packages, string name = "The RPG & West of Loathing",
        string start = "2000-01-01", string end = "2026-10-19") =>
        JsonSerializer.SerializeToElement(new { bundle_id = BundleId, bundle_name = name, date_start = start, date_end = end, sales, packages });

    private async Task<ItemIngestResult> IngestAsync(JsonElement item)
    {
        await using var db = NewContext();
        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestSteamworksBundleSalesItemAsync(BundleId, DateTimeOffset.UtcNow, item);
        await db.SaveChangesAsync();
        return result;
    }

    private static readonly object[] Days =
    [
        Sale("2025-08-07", 254, -5, 2954.5395, -50.0400, 318.0714, -5.3013),
        Sale("2025-08-08", 140, -9, 1650.5533, -85.3001, 177.3602, -9.4751),
    ];

    [Fact]
    public async Task StoresBothExportsWithPositiveReturnsAndUpdatesInPlace()
    {
        var first = await IngestAsync(Item(Days, [Package("2025-08-07", 254, -5, 2954.5395, -50.04)]));
        var again = await IngestAsync(Item([Sale("2025-08-08", 141, -9, 1660.0, -85.3001, 178.0, -9.4751)], null, name: "The RPG & WoL"));
        var same = await IngestAsync(Item(Days.Take(1).ToArray(), [Package("2025-08-07", 254, -5, 2954.5395, -50.04)], name: "The RPG & WoL"));

        Assert.Equal((3, 0), (first.Inserted, first.Updated));
        Assert.Equal((0, 1), (again.Inserted, again.Updated));
        Assert.Equal((0, 0), (same.Inserted, same.Updated));
        await using var db = NewContext();
        var aug7 = await db.SteamworksBundleSales.SingleAsync(s => s.DateLocal == new DateOnly(2025, 8, 7));
        Assert.Equal((254, 5, 2954.5395m, 50.04m, 5.3013m), (aug7.UnitsSold, aug7.UnitsReturned, aug7.GrossSalesUsd, aug7.GrossReturnsUsd, aug7.DiscountReturnsUsd));
        Assert.Equal(141, (await db.SteamworksBundleSales.SingleAsync(s => s.DateLocal == new DateOnly(2025, 8, 8))).UnitsSold);
        Assert.Equal("The RPG & WoL", (await db.SteamworksBundles.SingleAsync()).Name);
        Assert.Equal(5, (await db.SteamworksBundlePackageSales.SingleAsync()).UnitsReturned);
    }

    [Fact]
    public async Task BadRowsAreCountedAndAnInvalidWindowIsSkipped()
    {
        var bad = await IngestAsync(Item([Days[0], new { date_local = "2025-08-08", partner_id = 216530, units_sold = "many" }], null));
        var noWindow = await IngestAsync(Item(Days, null, start: "2026-10-19", end: "2026-10-01"));

        Assert.Equal((2, 1, 1), (bad.Processed, bad.Inserted, bad.Failed));
        Assert.Equal((0, 0), (noWindow.Processed, noWindow.Inserted));
    }

    [Fact]
    public async Task QueriesSumTheRangeAndFindThePackageGame()
    {
        var gameId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.Games.Add(new Game { Id = gameId, AppId = 2429270, Name = "The RPG", GameType = "Owned" });
            seed.SteamSaleFinancials.Add(new SteamSaleFinancial
            {
                Id = Guid.NewGuid(), GameId = gameId, SaleDate = new DateOnly(2025, 8, 7), CountryCode = "US",
                PackageId = PackageId, BundleId = BundleId, SalesUnits = 1,
            });
            await seed.SaveChangesAsync();
        }
        await IngestAsync(Item(Days, [Package("2025-08-07", 254, -5, 2954.5395, -50.04), Package("2025-08-08", 140, -9, 1650.5533, -85.3001)]));

        await using var db = NewContext();
        var service = new GameDataService(db);
        var all = await service.GetBundlesAsync(new GetBundlesQuery(null, null));
        var oneDay = await service.GetBundlesAsync(new GetBundlesQuery(new DateOnly(2025, 8, 8), new DateOnly(2025, 8, 8)));
        var detail = await service.GetBundleAsync(new GetBundleQuery(BundleId, null, null));

        Assert.Equal(new DateOnly(2025, 8, 8), all.LatestDate);
        var bundle = Assert.Single(all.Bundles);
        Assert.Equal(("The RPG & West of Loathing", 394, 14, 4605.0928m), (bundle.Name, bundle.UnitsSold, bundle.UnitsReturned, bundle.GrossSalesUsd));
        Assert.Equal(140, Assert.Single(oneDay.Bundles).UnitsSold);
        Assert.Equal([new DateOnly(2025, 8, 7), new DateOnly(2025, 8, 8)], detail.Days.Select(d => d.Date).ToList());
        var package = Assert.Single(detail.Packages);
        Assert.Equal((PackageId, (int?)2429270, "The RPG", (Guid?)gameId, 394), (package.PackageId, package.AppId, package.GameName, package.GameId, package.UnitsSold));
    }
}
