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
/// Covers the sales read side (<see cref="GameDataService.GetSalesDailyAsync"/>,
/// <see cref="GameDataService.GetSalesCountriesAsync"/>,
/// <see cref="GameDataService.GetSalesSummaryAsync"/>) and the country/bundle
/// reference data the financial ingestion keeps.
/// </summary>
public class SalesQueryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public SalesQueryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private static readonly Guid Chef = Guid.NewGuid();
    private static readonly Guid Rpg = Guid.NewGuid();

    private static SteamSaleFinancial Sale(Guid gameId, string date, string country, int units, decimal net, int returned = 0) => new()
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        SaleDate = DateOnly.Parse(date),
        CountryCode = country,
        Platform = "Windows",
        SalesUnits = units,
        ReturnsUnits = returned,
        GrossRevenueUsd = net * 1.2m,
        NetRevenueUsd = net,
    };

    private async Task SeedAsync()
    {
        await using var db = NewContext();
        db.Games.AddRange(
            new Game { Id = Chef, AppId = 2390230, Name = "The Chef's Shift", GameType = "Owned" },
            new Game { Id = Rpg, AppId = 2429270, Name = "The RPG", GameType = "Owned" });
        db.SteamCountries.AddRange(
            new SteamCountry { CountryCode = "US", CountryName = "United States", Region = "North America" },
            new SteamCountry { CountryCode = "DE", CountryName = "Germany", Region = "Western Europe" });
        db.SteamSaleFinancials.AddRange(
            Sale(Chef, "2026-08-01", "US", 10, 100.5m),
            Sale(Chef, "2026-10-04", "US", 2, 20.25m),
            Sale(Chef, "2026-10-04", "US", 1, 9.75m),   // same key, second line
            Sale(Chef, "2026-10-04", "DE", 3, 30.0001m, returned: 1),
            Sale(Chef, "2026-10-05", "XK", 1, 5m),       // no steam_country row
            Sale(Rpg, "2026-10-05", "DE", 4, 40m));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task DailySumsEveryLinePerDate()
    {
        await SeedAsync();
        await using var db = NewContext();

        var days = await new GameDataService(db).GetSalesDailyAsync(
            new GetSalesDailyQuery(2390230, new DateOnly(2026, 10, 1), null));

        Assert.Equal([new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 5)], days.Select(d => d.Date));
        var oct4 = days[0];
        Assert.Equal(6, oct4.GrossUnits);
        Assert.Equal(1, oct4.ReturnedUnits);
        Assert.Equal(5, oct4.NetUnits);
        Assert.Equal(60.0001m, oct4.NetUsd);
    }

    [Fact]
    public async Task CountriesCarryNameRegionAndFallBackToTheCode()
    {
        await SeedAsync();
        await using var db = NewContext();

        var dto = await new GameDataService(db).GetSalesCountriesAsync(
            new GetSalesCountriesQuery(2390230, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));

        Assert.Equal(new DateOnly(2026, 8, 1), dto.FirstDate);
        Assert.Equal(new DateOnly(2026, 10, 5), dto.LastDate);
        // DE 30.0001 edges out US 30.00 (two lines on one key, both counted).
        Assert.Equal(["DE", "US", "XK"], dto.Countries.Select(c => c.CountryCode));
        var us = dto.Countries[1];
        Assert.Equal(("United States", "North America", 3, 30m), (us.CountryName, us.Region, us.NetUnits, us.NetUsd));
        var xk = dto.Countries[2];
        Assert.Equal(("XK", ""), (xk.CountryName, xk.Region));
    }

    [Fact]
    public async Task SummaryTotalsAndThirtyDayWindowPerGame()
    {
        await SeedAsync();
        await using var db = NewContext();

        var summary = await new GameDataService(db).GetSalesSummaryAsync();

        Assert.Equal(new DateOnly(2026, 10, 5), summary.LatestDate);
        Assert.Equal(["The Chef's Shift", "The RPG"], summary.Games.Select(g => g.Name));
        var chef = summary.Games[0];
        Assert.Equal(17, chef.GrossUnits);
        Assert.Equal(16, chef.NetUnits);
        Assert.Equal(165.5001m, chef.NetUsd);
        Assert.Equal(6, chef.NetUnits30d);     // the August sale is outside the window
        Assert.Equal(65.0001m, chef.NetUsd30d);
        Assert.Equal(new DateOnly(2026, 8, 1), chef.FirstDate);
    }

    [Fact]
    public async Task EmptySalesHaveNoLatestDate()
    {
        await using var db = NewContext();
        var summary = await new GameDataService(db).GetSalesSummaryAsync();
        Assert.Null(summary.LatestDate);
        Assert.Empty(summary.Games);
    }

    [Fact]
    public async Task FinancialIngestionKeepsCountriesAndBundlesCurrent()
    {
        await SeedAsync();

        object Item(string region, string bundleName) => new
        {
            sales_date = "2026/10/06",
            transactions = new[]
            {
                new { date = "2026/10/06", country_code = "AU", gross_units_sold = 1, net_sales_usd = "9.9900", bundleid = 57829 },
            },
            reference_data = new
            {
                country_info = new[] { new { country_code = "AU", name = (string?)null, country_name = "Australia", region } },
                bundle_info = new[] { new { bundleid = 57829, name = (string?)null, bundle_name = bundleName } },
            },
        };

        foreach (var (region, bundle) in new[] { ("Oceania", "The RPG & West of Loathing"), ("Oceania", "The RPG and West of Loathing") })
        {
            await using var db = NewContext();
            var service = new IngestionService(db, NullLogger<IngestionService>.Instance);
            // Two items in one batch: the second must reuse the cached rows, not add them again.
            await service.IngestFinancialDataItemAsync(2390230, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(Item(region, bundle)));
            await service.IngestFinancialDataItemAsync(2390230, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(Item(region, bundle)));
            await db.SaveChangesAsync();
        }

        await using var check = NewContext();
        var au = await check.SteamCountries.SingleAsync(c => c.CountryCode == "AU");
        Assert.Equal(("Australia", "Oceania"), (au.CountryName, au.Region));
        var bundleRow = await check.SteamworksBundles.SingleAsync();
        Assert.Equal((57829, "The RPG and West of Loathing"), (bundleRow.BundleId, bundleRow.Name));
        Assert.NotEqual(default, bundleRow.UpdatedAt);
    }
}
