using System.Reflection;
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
/// Covers the Steam financial ingestion (<c>steam_financial</c> batches and the manual
/// upload). Steam sends several lines per (date, country, package, platform): the
/// same package standalone and in a bundle, or at two prices. They all count, so
/// ingestion replaces a game's lines per date instead of de-duplicating on a key.
/// </summary>
public class FinancialIngestionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public FinancialIngestionTests()
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
    private static readonly DateTimeOffset ScrapedAt = new(2026, 10, 14, 4, 0, 0, TimeSpan.Zero);

    private async Task SeedGameAsync()
    {
        await using var db = NewContext();
        db.Games.Add(new Game { Id = GameId, AppId = AppId, Name = "The Chef's Shift", GameType = "Owned" });
        await db.SaveChangesAsync();
    }

    private static object Line(string date, string country, int units, string netUsd, int? bundleId = null, string salePrice = "12.99") => new
    {
        date,
        line_item_type = "Package",
        packageid = 874080,
        package_sale_type = bundleId is null ? "Steam" : "Bundle",
        platform = "Windows",
        country_code = country,
        base_price = "12.99",
        sale_price = salePrice,
        currency = "USD",
        gross_units_sold = units,
        gross_units_returned = 0,
        gross_sales_usd = netUsd,
        gross_returns_usd = "0.0000",
        net_tax_usd = "0.0000",
        primary_appid = AppId,
        appid = AppId,
        net_units_sold = units,
        net_sales_usd = netUsd,
        partnerid = 216530,
        bundleid = bundleId,
    };

    private static JsonElement Item(string salesDate, params object[] lines) =>
        JsonSerializer.SerializeToElement(new { transactions = lines, reference_data = new { }, sales_date = salesDate });

    // An item of the multi-partner collector: data.partner_id is the partner of the key.
    private static JsonElement PartnerItem(object partnerId, string salesDate, params object[] lines) =>
        JsonSerializer.SerializeToElement(new { transactions = lines, reference_data = new { }, sales_date = salesDate, partner_id = partnerId });

    private const int PlanB = 216530;
    private const int Planzet = 337008;

    private async Task<List<(int Partner, int Units)>> UnitsByPartnerAsync() =>
        (await SalesAsync()).OrderBy(s => s.ReportPartnerId).Select(s => (s.ReportPartnerId, s.SalesUnits)).ToList();

    private async Task<ItemIngestResult> IngestAsync(params JsonElement[] items)
    {
        await using var db = NewContext();
        var service = new IngestionService(db, NullLogger<IngestionService>.Instance);
        int processed = 0, inserted = 0, failed = 0;
        foreach (var item in items)
        {
            var r = await service.IngestFinancialDataItemAsync(AppId, ScrapedAt, item);
            processed += r.Processed;
            inserted += r.Inserted;
            failed += r.Failed;
        }
        await db.SaveChangesAsync();
        return new ItemIngestResult(processed, inserted, 0, failed);
    }

    private async Task<List<SteamSaleFinancial>> SalesAsync()
    {
        await using var db = NewContext();
        return await db.SteamSaleFinancials.Where(s => s.GameId == GameId)
            .OrderBy(s => s.SaleDate).ThenBy(s => s.NetRevenueUsd).ToListAsync();
    }

    [Fact]
    public async Task LinesSharingTheOldKeyAreAllKept()
    {
        await SeedGameAsync();

        // Same date, country, package and platform: standalone and in bundle 57829.
        var result = await IngestAsync(Item("2026/10/05",
            Line("2026/10/05", "US", 2, "25.9800"),
            Line("2026/10/05", "US", 1, "11.6900", bundleId: 57829, salePrice: "11.69")));

        Assert.Equal(2, result.Inserted);
        var sales = await SalesAsync();
        Assert.Equal(2, sales.Count);
        Assert.Equal(37.67m, sales.Sum(s => s.NetRevenueUsd));
        Assert.Equal(57829, sales.Single(s => s.BundleId is not null).BundleId);
        Assert.All(sales, s => Assert.Equal(AppId, s.PrimaryAppId));
        Assert.All(sales, s => Assert.Equal("Package", s.LineItemType));
    }

    [Fact]
    public async Task ReSentDateReplacesInsteadOfDuplicating()
    {
        await SeedGameAsync();
        await IngestAsync(Item("2026/10/05", Line("2026/10/05", "US", 2, "25.9800"), Line("2026/10/05", "DE", 1, "12.9900")));

        await IngestAsync(Item("2026/10/05", Line("2026/10/05", "US", 2, "25.9800"), Line("2026/10/05", "DE", 1, "12.9900")));

        Assert.Equal(2, (await SalesAsync()).Count);
    }

    [Fact]
    public async Task RestatedDateUpdatesTotalsAndLeavesOtherDates()
    {
        await SeedGameAsync();
        await IngestAsync(
            Item("2026/10/04", Line("2026/10/04", "US", 5, "64.9500")),
            Item("2026/10/05", Line("2026/10/05", "US", 2, "25.9800"), Line("2026/10/05", "DE", 1, "12.9900")));

        // Steam restates 10-05: the DE sale was refunded away, US grew.
        await IngestAsync(Item("2026/10/05", Line("2026/10/05", "US", 3, "38.9700")));

        var sales = await SalesAsync();
        Assert.Equal(2, sales.Count);
        Assert.Equal(64.95m, sales.Single(s => s.SaleDate == new DateOnly(2026, 10, 4)).NetRevenueUsd);
        var restated = sales.Single(s => s.SaleDate == new DateOnly(2026, 10, 5));
        Assert.Equal(3, restated.SalesUnits);
        Assert.Equal(38.97m, restated.NetRevenueUsd);
    }

    [Fact]
    public async Task SameDateTwiceInOneBatchIsStoredOnce()
    {
        await SeedGameAsync();

        // GetChangedDatesForPartner repeats dates; both copies can share one batch.
        await IngestAsync(
            Item("2026/10/05", Line("2026/10/05", "US", 2, "25.9800")),
            Item("2026/10/05", Line("2026/10/05", "US", 2, "25.9800")));

        Assert.Single(await SalesAsync());
    }

    [Fact]
    public async Task MalformedLinesAreCountedNotThrown()
    {
        await SeedGameAsync();

        var result = await IngestAsync(Item("2026/10/05",
            Line("2026/10/05", "US", 2, "25.9800"),
            new { date = "not-a-date", country_code = "US" },
            new { country_code = "US" }));

        Assert.Equal(3, result.Processed);
        Assert.Equal(2, result.Failed);
        Assert.Single(await SalesAsync());
    }

    [Fact]
    public async Task NegativeReturnsAreStoredPositiveAndActivationsApart()
    {
        await SeedGameAsync();

        // Real shapes from GetDetailedSales on 2026-10-05: returns are negative, and
        // key activations come as their own lines with no sale and no revenue.
        await IngestAsync(Item("2026/10/05",
            new
            {
                date = "2026/10/05", country_code = "CN", packageid = 874080, platform = "Windows",
                gross_units_sold = 292, gross_units_returned = -17, net_units_sold = 275,
                gross_sales_usd = "1515.7034", gross_returns_usd = "-88.2300", net_tax_usd = "0.0000",
                net_sales_usd = "1427.4734",
            },
            new
            {
                date = "2026/10/05", country_code = "US", packageid = 874080, platform = "Windows",
                gross_units_activated = 2, net_units_sold = (int?)null, net_sales_usd = (string?)null,
            }));

        var sales = await SalesAsync();
        var sale = sales.Single(s => s.CountryCode == "CN");
        Assert.Equal((292, 17, 0), (sale.SalesUnits, sale.ReturnsUnits, sale.ActivatedUnits));
        Assert.Equal(88.23m, sale.GrossReturnsUsd);
        var keys = sales.Single(s => s.CountryCode == "US");
        Assert.Equal((0, 0, 2, 0m), (keys.SalesUnits, keys.ReturnsUnits, keys.ActivatedUnits, keys.NetRevenueUsd));

        await using var db = NewContext();
        var day = Assert.Single(await new GameDataService(db).GetSalesDailyAsync(new GetSalesDailyQuery(AppId, null, null)));
        Assert.Equal((292, 17, 275, 2), (day.GrossUnits, day.ReturnedUnits, day.NetUnits, day.ActivatedUnits));
        Assert.Equal(1427.4734m, day.NetUsd);
    }

    // A key activation line of key request keyRequestId (no sale, no revenue).
    private static object KeyLine(string date, string country, int activated, int? keyRequestId) => new
    {
        date, country_code = country, packageid = 874080, platform = "Windows",
        gross_units_activated = activated, key_request_id = keyRequestId, primary_appid = AppId,
    };

    // Real shapes of reference_data.key_request_info: a free license is a synthetic request
    // with no game code and no territory; a real key request has both.
    private static object FreeRequest(int id) => new { key_request_id = id, key_request_notes = "Synthetic request" };

    private static object KeyRequest(int id, string notes = "For participation in the October Humble Choice") => new
    {
        key_request_id = id, key_request_notes = notes,
        game_code_id = 1234, game_code_description = "Toy Shire",
        territory_code_id = 0, territory_code_description = "Worldwide",
    };

    private static JsonElement ItemWithRequests(string salesDate, object[] requests, params object[] lines) =>
        JsonSerializer.SerializeToElement(new { transactions = lines, reference_data = new { key_request_info = requests }, sales_date = salesDate });

    [Fact]
    public async Task KeyRequestsAreStoredAndUpdated()
    {
        await SeedGameAsync();

        await IngestAsync(ItemWithRequests("2026/10/05", [FreeRequest(1007134), KeyRequest(2001)]));
        (DateTime Key, DateTime Free) firstUpdatedAt;

        await using (var db = NewContext())
        {
            var requests = await db.SteamKeyRequests.OrderBy(k => k.KeyRequestId).ToListAsync();
            Assert.Equal([(2001, false), (1007134, true)], requests.Select(k => (k.KeyRequestId, k.IsFreeLicense)));
            var key = requests[0];
            Assert.Equal(("For participation in the October Humble Choice", "Toy Shire", "Worldwide"),
                (key.Notes, key.GameCodeDescription, key.TerritoryDescription));
            Assert.Equal("Synthetic request", requests[1].Notes);
            Assert.NotEqual(default, key.UpdatedAt);
            firstUpdatedAt = (key.UpdatedAt, requests[1].UpdatedAt);
        }

        // A later batch flips both stored requests; " synthetic REQUEST " is still free.
        await IngestAsync(ItemWithRequests("2026/10/06",
            [FreeRequest(2001), KeyRequest(1007134, "Press keys"),
             new { key_request_id = 3001, key_request_notes = " synthetic REQUEST ", game_code_id = 5 }]));

        await using (var db = NewContext())
        {
            var requests = await db.SteamKeyRequests.OrderBy(k => k.KeyRequestId).ToListAsync();
            Assert.Equal([(2001, true), (3001, true), (1007134, false)], requests.Select(k => (k.KeyRequestId, k.IsFreeLicense)));
            Assert.Equal(("Synthetic request", (string?)null, (string?)null),
                (requests[0].Notes, requests[0].GameCodeDescription, requests[0].TerritoryDescription));
            Assert.Equal(("Press keys", "Toy Shire", "Worldwide"),
                (requests[2].Notes, requests[2].GameCodeDescription, requests[2].TerritoryDescription));
            Assert.True(requests[0].UpdatedAt > firstUpdatedAt.Key);
            Assert.True(requests[2].UpdatedAt > firstUpdatedAt.Free);
        }
    }

    [Fact]
    public async Task DailySplitsKeyActivationsFromFreeLicenses()
    {
        await SeedGameAsync();

        await IngestAsync(ItemWithRequests("2026/10/05", [FreeRequest(1007134), KeyRequest(2001)],
            KeyLine("2026/10/05", "US", 500, 1007134),
            KeyLine("2026/10/05", "DE", 7, 2001),
            KeyLine("2026/10/05", "FR", 3, null),
            Line("2026/10/05", "US", 2, "25.9800")));

        await using var db = NewContext();
        var day = Assert.Single(await new GameDataService(db).GetSalesDailyAsync(new GetSalesDailyQuery(AppId, null, null)));
        Assert.Equal((2, 10, 500), (day.GrossUnits, day.ActivatedUnits, day.FreeLicenseUnits));
    }

    [Fact]
    public async Task SummarySplitsKeyActivationsFromFreeLicenses()
    {
        await SeedGameAsync();

        await IngestAsync(
            ItemWithRequests("2026/10/05", [FreeRequest(1007134), KeyRequest(2001)],
                KeyLine("2026/10/05", "US", 500, 1007134), KeyLine("2026/10/05", "DE", 7, 2001)),
            ItemWithRequests("2026/10/06", [], KeyLine("2026/10/06", "US", 40, 1007134), KeyLine("2026/10/06", "FR", 3, null)));

        await using var db = NewContext();
        var game = Assert.Single((await new GameDataService(db).GetSalesSummaryAsync()).Games);
        Assert.Equal((10, 540), (game.ActivatedUnits, game.FreeLicenseUnits));
    }

    [Fact]
    public async Task UnknownKeyRequestCountsAsKeyActivation()
    {
        await SeedGameAsync();

        // Existing rows before the reference data arrives: the request is not stored yet.
        await IngestAsync(Item("2026/10/05", KeyLine("2026/10/05", "US", 500, 1007134)));

        await using (var db = NewContext())
        {
            var day = Assert.Single(await new GameDataService(db).GetSalesDailyAsync(new GetSalesDailyQuery(AppId, null, null)));
            Assert.Equal((500, 0), (day.ActivatedUnits, day.FreeLicenseUnits));
        }

        // A resync of another date brings the request: the stored line now counts as free.
        await IngestAsync(ItemWithRequests("2026/10/06", [FreeRequest(1007134)]));

        await using (var db = NewContext())
        {
            var day = Assert.Single(await new GameDataService(db).GetSalesDailyAsync(new GetSalesDailyQuery(AppId, null, null)));
            Assert.Equal((0, 500), (day.ActivatedUnits, day.FreeLicenseUnits));
        }
    }

    [Fact]
    public async Task FourDecimalAmountsArePreserved()
    {
        await SeedGameAsync();

        await IngestAsync(Item("2026/09/02", Line("2026/09/02", "US", 3, "43.5558")));

        Assert.Equal(43.5558m, Assert.Single(await SalesAsync()).NetRevenueUsd);
    }

    [Fact]
    public async Task ManualUploadReplacesTheDatesItCovers()
    {
        await SeedGameAsync();
        await IngestAsync(Item("2026/10/05", Line("2026/10/05", "US", 2, "25.9800"), Line("2026/10/05", "DE", 1, "12.9900")));

        var json = JsonSerializer.Serialize(new
        {
            transactions = new[] { Line("2026/10/05", "US", 4, "51.9600"), Line("2026/10/06", "US", 1, "12.9900") }
        });
        IngestionResult upload;
        await using (var db = NewContext())
            upload = await new IngestionService(db, NullLogger<IngestionService>.Instance).IngestFinancialDataAsync(
                new IngestFinancialDataCommand(AppId, "sales.json", new MemoryStream(Encoding.UTF8.GetBytes(json))));

        Assert.Equal(2, upload.RecordsInserted);
        var sales = await SalesAsync();
        Assert.Equal(2, sales.Count);
        Assert.Equal(4, sales.Single(s => s.SaleDate == new DateOnly(2026, 10, 5)).SalesUnits);
    }

    [Fact]
    public async Task TwoPartnersOnOneGameAndDateBothStay()
    {
        await SeedGameAsync();

        // A game moved to another partner mid-day: both partners report lines for the date.
        await IngestAsync(PartnerItem(PlanB, "2025/08/01", Line("2025/08/01", "US", 2, "25.9800")));
        await IngestAsync(PartnerItem(Planzet, "2025/08/01", Line("2025/08/01", "US", 3, "38.9700")));

        var sales = await SalesAsync();
        Assert.Equal([(PlanB, 2), (Planzet, 3)], sales.Select(s => (s.ReportPartnerId, s.SalesUnits)));
        // PartnerId stays Steam's line value; the Line helper always sends 216530.
        Assert.All(sales, s => Assert.Equal(PlanB, s.PartnerId));
    }

    [Fact]
    public async Task ResendForOnePartnerReplacesOnlyThatPartner()
    {
        await SeedGameAsync();
        await IngestAsync(
            PartnerItem(PlanB, "2025/08/01", Line("2025/08/01", "US", 2, "25.9800")),
            PartnerItem(Planzet, "2025/08/01", Line("2025/08/01", "US", 3, "38.9700"), Line("2025/08/01", "DE", 1, "12.9900")));

        // 337008 restates the date: one line now.
        await IngestAsync(PartnerItem(Planzet, "2025/08/01", Line("2025/08/01", "US", 4, "51.9600")));

        var sales = await SalesAsync();
        Assert.Equal([(PlanB, 2), (Planzet, 4)], sales.Select(s => (s.ReportPartnerId, s.SalesUnits)));
    }

    [Fact]
    public async Task OneBatchWithBothPartnersAndARepeatedCopyKeepsTwoSets()
    {
        await SeedGameAsync();

        // Pile Up!-like batch: 216530, then 337008, then the repeated 216530 copy. The second
        // 216530 copy must drop only the first 216530 copy's staged lines, not 337008's.
        await IngestAsync(
            PartnerItem(PlanB, "2025/08/01", Line("2025/08/01", "US", 2, "25.9800")),
            PartnerItem(Planzet, "2025/08/01", Line("2025/08/01", "US", 3, "38.9700")),
            PartnerItem(PlanB, "2025/08/01", Line("2025/08/01", "US", 2, "25.9800")));

        var sales = await SalesAsync();
        Assert.Equal([(PlanB, 2), (Planzet, 3)], sales.Select(s => (s.ReportPartnerId, s.SalesUnits)));
        Assert.Equal(64.95m, sales.Sum(s => s.NetRevenueUsd));
    }

    [Fact]
    public async Task ItemWithoutPartnerIdCountsAs216530()
    {
        await SeedGameAsync();
        await IngestAsync(PartnerItem(Planzet, "2025/08/01", Line("2025/08/01", "US", 3, "38.9700")));

        // The collector before partners sends no partner_id: it replaces 216530's lines only.
        await IngestAsync(Item("2025/08/01", Line("2025/08/01", "US", 2, "25.9800")));
        await IngestAsync(Item("2025/08/01", Line("2025/08/01", "US", 5, "64.9500")));

        Assert.Equal([(PlanB, 5), (Planzet, 3)], await UnitsByPartnerAsync());
    }

    [Fact]
    public async Task NullPartnerIdCountsAs216530()
    {
        await SeedGameAsync();

        await IngestAsync(PartnerItem(null!, "2025/08/01", Line("2025/08/01", "US", 2, "25.9800")));

        Assert.Equal(PlanB, Assert.Single(await SalesAsync()).ReportPartnerId);
    }

    [Theory]
    [InlineData("\"337008\"")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.5")]
    public async Task InvalidPartnerIdFailsTheLinesAndWritesNothing(string partnerJson)
    {
        await SeedGameAsync();
        await IngestAsync(Item("2025/08/01", Line("2025/08/01", "US", 2, "25.9800")));

        var result = await IngestAsync(PartnerItem(JsonDocument.Parse(partnerJson).RootElement, "2025/08/01",
            Line("2025/08/01", "US", 3, "38.9700"), Line("2025/08/01", "DE", 1, "12.9900")));

        Assert.Equal((2, 0, 2), (result.Processed, result.Inserted, result.Failed));
        var sale = Assert.Single(await SalesAsync());
        Assert.Equal((PlanB, 2), (sale.ReportPartnerId, sale.SalesUnits));
    }

    [Fact]
    public async Task ManualUploadReplacesOnlyItsPartner()
    {
        await SeedGameAsync();
        await IngestAsync(
            PartnerItem(PlanB, "2025/08/01", Line("2025/08/01", "US", 2, "25.9800")),
            PartnerItem(Planzet, "2025/08/01", Line("2025/08/01", "US", 3, "38.9700")));

        async Task UploadAsync(int? partnerId, int units)
        {
            var json = JsonSerializer.Serialize(new { transactions = new[] { Line("2025/08/01", "US", units, "1.0000") } });
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await using var db = NewContext();
            var command = partnerId is { } id
                ? new IngestFinancialDataCommand(AppId, "sales.json", stream, id)
                : new IngestFinancialDataCommand(AppId, "sales.json", stream);
            await new IngestionService(db, NullLogger<IngestionService>.Instance).IngestFinancialDataAsync(command);
        }

        await UploadAsync(Planzet, 7);
        Assert.Equal([(PlanB, 2), (Planzet, 7)], await UnitsByPartnerAsync());

        // Without a partner the upload is 216530's.
        await UploadAsync(null, 9);
        Assert.Equal([(PlanB, 9), (Planzet, 7)], await UnitsByPartnerAsync());
    }

    [Fact]
    public async Task LegacyDispatcherFilesAreFor216530()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{\"transactions\":[]}");
            var mediator = DispatchProxy.Create<MediatR.IMediator, CapturingMediator>();
            var dispatcher = new IngestionDispatcher(mediator, NullLogger<IngestionDispatcher>.Instance);

            await dispatcher.DispatchAsync(JsonSerializer.Serialize(new { source = "steam_financial", appId = AppId, filePath = path }), default);

            var command = Assert.IsType<IngestFinancialDataCommand>(((CapturingMediator)(object)mediator).Sent);
            Assert.Equal((AppId, PlanB), (command.AppId, command.ReportPartnerId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    public class CapturingMediator : DispatchProxy
    {
        public object? Sent { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Sent = args?[0];
            return Task.FromResult(new IngestionResult(0, 0, 0, 0, Guid.Empty));
        }
    }

    [Fact]
    public async Task FailedBatchIsLoggedAsFailedNotLeftRunning()
    {
        await using var db = NewContext();
        // A handler that stages a row the database rejects (no such game: FK).
        var service = DispatchProxy.Create<IIngestionService, OrphanRowIngestion>();
        ((OrphanRowIngestion)(object)service).Db = db;
        var dispatcher = new BatchIngestionDispatcher(db, service, NullLogger<BatchIngestionDispatcher>.Instance);

        var message = JsonSerializer.Serialize(new
        {
            batch_id = Guid.NewGuid(),
            scraped_at = ScrapedAt,
            schema_version = 1,
            source = "steam_financial",
            items = new[] { new { app_id = AppId, scraped_at = ScrapedAt, data = new { } } },
        });

        await Assert.ThrowsAnyAsync<Exception>(() => dispatcher.DispatchAsync(message, CancellationToken.None));

        await using var check = NewContext();
        var log = Assert.Single(await check.IngestionLogs.ToListAsync());
        Assert.Equal("Failed", log.Status);
        Assert.NotNull(log.EndTime);
        Assert.Empty(await check.SteamSaleFinancials.ToListAsync());
    }

    public class OrphanRowIngestion : DispatchProxy
    {
        public CatoDbContext Db { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IIngestionService.IngestFinancialDataItemAsync))
                throw new NotSupportedException(targetMethod?.Name);

            Db.SteamSaleFinancials.Add(new SteamSaleFinancial
            {
                Id = Guid.NewGuid(),
                GameId = Guid.NewGuid(),
                SaleDate = new DateOnly(2026, 10, 5),
                CountryCode = "US",
            });
            return Task.FromResult(new ItemIngestResult(1, 1, 0, 0));
        }
    }
}
