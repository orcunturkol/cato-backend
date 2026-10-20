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
/// Covers the <c>steamworks_players</c> item: daily DAU/PCU history into
/// <c>active_users_history</c>, yesterday's players by country, and the app details
/// snapshots of a game and its demo. Figures are from 2390230 on 2026-10-20.
/// </summary>
public class SteamworksPlayersTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public SteamworksPlayersTests()
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
        db.SteamCountries.Add(new SteamCountry { CountryCode = "CN", CountryName = "China", Region = "Asia" });
        db.SteamCountries.Add(new SteamCountry { CountryCode = "US", CountryName = "United States", Region = "North America" });
        await db.SaveChangesAsync();
    }

    private static object Day(string date, int pcu, int dau) => new { date, pcu, dau };
    private static object Country(string code, string name, double share) => new { country_code = code, country = name, share_percent = share };

    private static JsonElement Item(object[]? days = null, string? countriesDate = null, object[]? countries = null,
        object? details = null, object? demo = null, string snapshot = "2026-10-20") =>
        JsonSerializer.SerializeToElement(new
        {
            game_name = "The Chef's Shift",
            snapshot_date = snapshot,
            days,
            countries_date = countriesDate,
            countries,
            details,
            demo_details = demo,
        });

    private static object Details(int appId, int? median, string? text, long? users, int? demoAppId = null) => new
    {
        app_id = appId,
        median_playtime_minutes = median,
        median_playtime_text = text,
        lifetime_unique_users = users,
        demo_app_id = demoAppId,
    };

    private async Task<ItemIngestResult> IngestAsync(JsonElement item)
    {
        await using var db = NewContext();
        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestSteamworksPlayersItemAsync(AppId, DateTimeOffset.UtcNow, item);
        await db.SaveChangesAsync();
        return result;
    }

    [Fact]
    public async Task DaysAreUpsertedByDateAndKeepManualRows()
    {
        await SeedAsync();
        await using (var seed = NewContext())
        {
            seed.ActiveUsersHistories.Add(new ActiveUsersHistory
            {
                Id = Guid.NewGuid(), GameId = GameId, RecordedAt = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc), Dau = 5, Mau = 40,
            });
            await seed.SaveChangesAsync();
        }

        var first = await IngestAsync(Item(days: [Day("2026-10-18", 566, 3934), Day("2026-10-19", 550, 3707)]));
        var again = await IngestAsync(Item(days: [Day("2026-10-18", 566, 3934), Day("2026-10-19", 560, 3800)]));
        var same = await IngestAsync(Item(days: [Day("2026-10-19", 560, 3800)]));

        Assert.Equal((2, 0), (first.Inserted, first.Updated));
        Assert.Equal((0, 1), (again.Inserted, again.Updated));
        Assert.Equal((0, 0), (same.Inserted, same.Updated));
        await using var db = NewContext();
        var rows = await db.ActiveUsersHistories.OrderBy(a => a.RecordedAt).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal((5, 40, (int?)null), (rows[0].Dau, rows[0].Mau, rows[0].PeakConcurrentUsers));
        Assert.Equal((3800, (int?)560), (rows[2].Dau, rows[2].PeakConcurrentUsers));
        Assert.All(rows.Skip(1), r => Assert.NotEqual(default, r.UpdatedAt));
    }

    [Fact]
    public async Task CountriesOfADateAreReplaced()
    {
        await SeedAsync();
        await IngestAsync(Item(countriesDate: "2026-10-18", countries: [Country("CN", "China", 80.0)]));
        await IngestAsync(Item(countriesDate: "2026-10-19", countries: [Country("CN", "China", 77.15), Country("JP", "Japan", 1.66)]));

        var again = await IngestAsync(Item(countriesDate: "2026-10-19", countries: [Country("CN", "China", 77.15), Country("US", "United States", 6.47)]));

        Assert.Equal((1, 0), (again.Inserted, again.Updated));
        await using var db = NewContext();
        var oct19 = await db.SteamworksPlayerCountries.Where(c => c.SnapshotDate == new DateOnly(2026, 10, 19))
            .OrderBy(c => c.CountryCode).Select(c => c.CountryCode).ToListAsync();
        Assert.Equal(["CN", "US"], oct19); // Japan dropped out of the re-fetch
        Assert.Equal(3, await db.SteamworksPlayerCountries.CountAsync());
    }

    [Fact]
    public async Task DetailsOfGameAndDemoAreDailySnapshots()
    {
        await SeedAsync();
        await IngestAsync(Item(details: Details(AppId, 223, "3 hours 43 minutes", 106261, DemoAppId),
            demo: Details(DemoAppId, 48, "48 minutes", 142872)));
        var again = await IngestAsync(Item(details: Details(AppId, 224, "3 hours 44 minutes", 106300, DemoAppId),
            demo: Details(DemoAppId, 48, "48 minutes", 142872)));
        await IngestAsync(Item(details: Details(AppId, null, "Not enough data", null), snapshot: "2026-10-21"));

        Assert.Equal((0, 1), (again.Inserted, again.Updated));
        await using var db = NewContext();
        var rows = await db.SteamworksAppDetails.OrderBy(d => d.SnapshotDate).ThenBy(d => d.AppId).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal((AppId, false, (int?)224), (rows[0].AppId, rows[0].IsDemo, rows[0].MedianPlaytimeMinutes));
        Assert.Equal((DemoAppId, true, GameId), (rows[1].AppId, rows[1].IsDemo, rows[1].GameId));
        Assert.Equal(((int?)null, "Not enough data"), (rows[2].MedianPlaytimeMinutes, rows[2].MedianPlaytimeText));
    }

    [Fact]
    public async Task NullPartsLeaveTablesAlone()
    {
        await SeedAsync();
        await IngestAsync(Item(days: [Day("2026-10-19", 550, 3707)], countriesDate: "2026-10-19", countries: [Country("CN", "China", 77.15)]));

        var result = await IngestAsync(Item());

        Assert.Equal((0, 0, 0), (result.Processed, result.Inserted, result.Failed));
        await using var db = NewContext();
        Assert.Equal(1, await db.ActiveUsersHistories.CountAsync());
        Assert.Equal(1, await db.SteamworksPlayerCountries.CountAsync());
    }

    [Fact]
    public async Task BadRowsAreCountedAndSkipped()
    {
        await SeedAsync();
        var result = await IngestAsync(Item(
            days: [Day("2026-10-19", 550, 3707), new { date = "not a date", pcu = 1, dau = 1 }, Day("2026-10-18", -1, 3)],
            countriesDate: "2026-10-19",
            countries: [Country("CN", "China", 77.15), Country("XX", "Nowhere", 120.0)]));

        Assert.Equal((5, 2, 3), (result.Processed, result.Inserted, result.Failed));
    }

    [Fact]
    public async Task QueryReturnsHistoryLatestCountriesAndDetails()
    {
        await SeedAsync();
        await IngestAsync(Item(
            days: [Day("2026-10-19", 550, 3707), Day("2026-10-18", 566, 3934)],
            countriesDate: "2026-10-18", countries: [Country("CN", "China", 80.0)]));
        await IngestAsync(Item(
            countriesDate: "2026-10-19",
            countries: [Country("US", "United States", 6.47), Country("CN", "China", 77.15), Country("ZZ", "Elsewhere", 0.03)],
            details: Details(AppId, 223, "3 hours 43 minutes", 106261, DemoAppId),
            demo: Details(DemoAppId, 48, "48 minutes", 142872)));
        await IngestAsync(Item(details: Details(AppId, 230, "3 hours 50 minutes", 107000, DemoAppId), snapshot: "2026-10-21"));

        await using var db = NewContext();
        var dto = await new GameDataService(db).GetPlayersAsync(new GetPlayersQuery(AppId));

        Assert.Equal([new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 19)], dto.Days.Select(d => d.Date).ToList());
        Assert.Equal((3934, (int?)566), (dto.Days[0].Dau, dto.Days[0].PeakConcurrentUsers));
        Assert.Equal(new DateOnly(2026, 10, 19), dto.CountriesDate);
        Assert.Equal(["CN", "US", "ZZ"], dto.Countries.Select(c => c.CountryCode).ToList());
        Assert.Equal(("Asia", ""), (dto.Countries[0].Region, dto.Countries[2].Region));
        Assert.Equal(2, dto.Details.Count);
        Assert.Equal((AppId, (int?)230, new DateOnly(2026, 10, 21)), (dto.Details[0].AppId, dto.Details[0].MedianPlaytimeMinutes, dto.Details[0].SnapshotDate));
        Assert.Equal((DemoAppId, true), (dto.Details[1].AppId, dto.Details[1].IsDemo));

        var unknown = await new GameDataService(db).GetPlayersAsync(new GetPlayersQuery(1));
        Assert.Empty(unknown.Days);
    }
}
