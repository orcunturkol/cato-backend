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
/// Covers the <c>countries</c> series of
/// <see cref="IngestionService.IngestSteamworksWishlistReportItemAsync"/> and
/// <see cref="GameDataService.GetSteamworksWishlistCountriesAsync"/>. On SQLite so the
/// unique index on (GameId, DateLocal, Country) is enforced: each run re-sends the
/// last weeks, and "update, never duplicate" is the requirement.
/// </summary>
public class SteamworksWishlistCountryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public SteamworksWishlistCountryTests()
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
    private static readonly DateTimeOffset ScrapedAt = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    private async Task SeedGameAsync()
    {
        await using var db = NewContext();
        db.Games.Add(new Game { Id = GameId, AppId = AppId, Name = "The Chef's Shift", GameType = "Owned" });
        await db.SaveChangesAsync();
    }

    private static object Row(string date, string country, string region, int adds, int deletes = 0, int purchases = 0, int gifts = 0) => new
    {
        date_local = date,
        country,
        region,
        adds,
        deletes,
        purchases_and_activations = purchases,
        gifts
    };

    private static JsonElement Payload(object[]? countries, object[]? actions = null) =>
        JsonSerializer.SerializeToElement(new { game_name = "The Chef's Shift", actions, countries });

    private async Task<ItemIngestResult> IngestAsync(JsonElement data)
    {
        await using var db = NewContext();
        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestSteamworksWishlistReportItemAsync(AppId, ScrapedAt, data);
        await db.SaveChangesAsync();
        return result;
    }

    private async Task<List<SteamworksWishlistCountry>> RowsAsync()
    {
        await using var db = NewContext();
        return await db.SteamworksWishlistCountries.Where(c => c.GameId == GameId)
            .OrderBy(c => c.DateLocal).ThenBy(c => c.Country).ToListAsync();
    }

    [Fact]
    public async Task InsertsCountryRows()
    {
        await SeedGameAsync();

        var result = await IngestAsync(Payload([
            Row("2026-09-01", "Austria", "Western Europe", 0, deletes: 1),
            Row("2026-09-01", "Brazil", "Latin America", 1, deletes: 1),
            Row("2026-09-02", "Brazil", "Latin America", 3, purchases: 1, gifts: 1),
        ]));

        Assert.Equal(3, result.Inserted);
        var rows = await RowsAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Latin America", rows[2].Region);
        Assert.Equal(1, rows[2].Gifts);
        Assert.NotEqual(default, rows[0].CreatedAt);
    }

    [Fact]
    public async Task ReSentWindowUpdatesInPlace()
    {
        await SeedGameAsync();
        await IngestAsync(Payload([
            Row("2026-09-01", "Brazil", "Latin America", 1),
            Row("2026-09-02", "Brazil", "Latin America", 3),
        ]));

        var second = await IngestAsync(Payload([
            Row("2026-09-02", "Brazil", "Latin America", 5),
            Row("2026-09-03", "Brazil", "Latin America", 2),
        ]));

        Assert.Equal(1, second.Inserted);
        Assert.Equal(1, second.Updated);
        var rows = await RowsAsync();
        Assert.Equal([1, 5, 2], rows.Select(r => r.Adds));
    }

    [Fact]
    public async Task IdenticalReSendWritesNothing()
    {
        await SeedGameAsync();
        object[] window = [Row("2026-09-01", "Brazil", "Latin America", 1)];
        await IngestAsync(Payload(window));

        var again = await IngestAsync(Payload(window));

        Assert.Equal(0, again.Inserted);
        Assert.Equal(0, again.Updated);
    }

    [Fact]
    public async Task DuplicateKeysWithinOnePayloadDoNotTripTheUniqueIndex()
    {
        await SeedGameAsync();

        var result = await IngestAsync(Payload([
            Row("2026-09-01", "Brazil", "Latin America", 1),
            Row("2026-09-01", "Brazil", "Latin America", 4),
        ]));

        Assert.Equal(1, result.Inserted);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(4, row.Adds);
    }

    [Fact]
    public async Task MalformedRowsAreSkippedAndBlankRegionIsKept()
    {
        await SeedGameAsync();

        var result = await IngestAsync(Payload([
            Row("2026-09-01", "", "Western Europe", 1),
            Row("not-a-date", "Austria", "Western Europe", 1),
            Row("2026-09-01", "Austria", "", 2),
        ]));

        Assert.Equal(3, result.Processed);
        Assert.Equal(2, result.Failed);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal("Austria", row.Country);
        Assert.Equal(string.Empty, row.Region);
    }

    [Fact]
    public async Task MissingCountriesLeaveTheTableUntouched()
    {
        await SeedGameAsync();
        await IngestAsync(Payload([Row("2026-09-01", "Brazil", "Latin America", 1)]));

        await IngestAsync(Payload(countries: null, actions: [new
        {
            date_local = "2026-09-02", adds = 1, deletes = 0, purchases_and_activations = 0, gifts = 0
        }]));

        Assert.Single(await RowsAsync());
    }

    [Fact]
    public async Task QuerySumsTheRangeAndReportsWholeCoverage()
    {
        await SeedGameAsync();
        await IngestAsync(Payload([
            Row("2026-08-31", "Brazil", "Latin America", 50),
            Row("2026-09-01", "Austria", "Western Europe", 2, deletes: 1),
            Row("2026-09-01", "Brazil", "Latin America", 1, purchases: 1),
            Row("2026-09-02", "Brazil", "Latin America", 3, gifts: 1),
            Row("2026-09-03", "Austria", "Western Europe", 9),
        ]));

        await using var db = NewContext();
        var dto = await new GameDataService(db).GetSteamworksWishlistCountriesAsync(
            new GetSteamworksWishlistCountriesQuery(AppId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)));

        Assert.Equal(new DateOnly(2026, 8, 31), dto.FirstDate);
        Assert.Equal(new DateOnly(2026, 9, 3), dto.LastDate);
        Assert.Equal(["Brazil", "Austria"], dto.Countries.Select(c => c.Country));
        var brazil = dto.Countries[0];
        Assert.Equal(4, brazil.Adds);
        Assert.Equal(1, brazil.PurchasesAndActivations);
        Assert.Equal(1, brazil.Gifts);
        Assert.Equal("Western Europe", dto.Countries[1].Region);
        Assert.Equal(1, dto.Countries[1].Deletes);
    }

    [Fact]
    public async Task QueryForAGameWithoutCountryRowsIsEmpty()
    {
        await SeedGameAsync();

        await using var db = NewContext();
        var dto = await new GameDataService(db).GetSteamworksWishlistCountriesAsync(
            new GetSteamworksWishlistCountriesQuery(AppId, null, null));

        Assert.Null(dto.FirstDate);
        Assert.Null(dto.LastDate);
        Assert.Empty(dto.Countries);
    }
}
