using Cato.API.Models.DbTables;
using Cato.API.Services.DbTables;
using Cato.API.Services.Handlers.DbTables;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Cato.API.Tests.DbTables;

public class DbTablesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;
    private readonly MutableClock _clock = new(new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));
    private readonly DbTableCountCache _cache = new();

    public DbTablesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private CatoDbContext NewContext() => new(_options);

    private DbTableStats Stats(CatoDbContext db) => new(db, _cache, _clock);

    private static IReadOnlyList<DbTableSchema> Schema(CatoDbContext db) =>
        DbTableSchemaReader.Read(db.GetService<IDesignTimeModel>().Model);

    private void AddCountries(params string[] codes)
    {
        using var db = NewContext();
        foreach (var code in codes)
            db.SteamCountries.Add(new SteamCountry { CountryCode = code, CountryName = code, Region = "" });
        db.SaveChanges();
    }

    [Fact]
    public void Catalog_documents_every_mapped_table_and_nothing_else()
    {
        using var db = NewContext();
        var mapped = Schema(db).Select(t => t.Table).Order().ToList();
        var documented = DbTableCatalog.Tables.Select(t => t.Table).Order().ToList();

        Assert.Equal(mapped, documented);
    }

    [Fact]
    public void Column_notes_name_real_properties()
    {
        using var db = NewContext();
        var properties = Schema(db).ToDictionary(t => t.Table, t => t.Columns.Select(c => c.Property).ToHashSet());

        var unknown = DbTableCatalog.Tables
            .SelectMany(t => (t.Columns?.Keys ?? []).Select(column => (t.Table, column)))
            .Where(x => !properties[x.Table].Contains(x.column))
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Sources_use_known_sites_and_a_producer_with_every_job()
    {
        var sites = typeof(DataSite).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet();
        var sources = DbTableCatalog.Tables.SelectMany(t => t.Sources).ToList();

        Assert.All(sources, s => Assert.Contains(s.Site, sites));
        Assert.All(sources.Where(s => s.JobName != null), s => Assert.NotNull(s.Producer));
        Assert.All(DbTableCatalog.Tables.Where(t => t.Collection != DataCollection.NoWriter),
            t => Assert.NotEmpty(t.Sources));
    }

    [Fact]
    public async Task List_has_every_table_without_live_counts_off_postgres()
    {
        await using var db = NewContext();
        var result = await new GetDbTablesHandler(db, Stats(db), _clock).Handle(new GetDbTablesQuery(), default);

        Assert.False(result.LiveCounts);
        Assert.Equal(DbTableCatalog.Tables.Count, result.Tables.Count);
        Assert.All(result.Tables, t => Assert.True(t.Documented));
        Assert.All(result.Tables, t => Assert.Null(t.ApproxRows));

        var daily = result.Tables.Single(t => t.Table == "steamworks_wishlist_daily");
        Assert.Equal("Unique key: GameId + DateLocal.", daily.Validation[0]);
        Assert.Contains(DataSite.Steamworks, daily.Sites);
    }

    [Fact]
    public async Task Detail_returns_null_for_a_name_that_is_not_a_mapped_table()
    {
        await using var db = NewContext();
        var handler = new GetDbTableHandler(db, Stats(db), _clock);

        Assert.Null(await handler.Handle(new GetDbTableQuery("pg_class"), default));
        Assert.Null(await handler.Handle(new GetDbTableQuery("main_game\"; DROP TABLE main_game; --"), default));
    }

    [Fact]
    public async Task Detail_counts_rows_and_lists_columns_key_first()
    {
        AddCountries("TR", "DE", "US");

        await using var db = NewContext();
        var detail = await new GetDbTableHandler(db, Stats(db), _clock).Handle(new GetDbTableQuery("steam_country"), default);

        Assert.NotNull(detail);
        Assert.Equal(3, detail.ExactRows);
        Assert.Equal(_clock.Now, detail.ExactCountedAt);
        Assert.Equal(["CountryCode", "CountryName", "CreatedAt", "Region", "UpdatedAt"], detail.Columns.Select(c => c.Column));
        Assert.True(detail.Columns[0].PrimaryKey);
        Assert.Equal("CATO: set on every save", detail.Columns.Single(c => c.Column == "UpdatedAt").Source);
        Assert.StartsWith("Steamworks: IPartnerFinancialsService", detail.Columns.Single(c => c.Column == "Region").Source);
    }

    [Fact]
    public async Task Exact_count_is_reused_for_five_minutes()
    {
        AddCountries("TR");
        await using var db = NewContext();
        var stats = Stats(db);

        Assert.Equal(1, (await stats.CountRowsAsync("steam_country", default)).Rows);

        AddCountries("DE");
        _clock.Now += TimeSpan.FromMinutes(4);
        Assert.Equal(1, (await stats.CountRowsAsync("steam_country", default)).Rows);

        _clock.Now += TimeSpan.FromMinutes(2);
        Assert.Equal(2, (await stats.CountRowsAsync("steam_country", default)).Rows);
    }

    [Fact]
    public async Task Column_notes_override_the_table_source()
    {
        await using var db = NewContext();
        var detail = await new GetDbTableHandler(db, Stats(db), _clock).Handle(new GetDbTableQuery("main_game"), default);

        var columns = detail!.Columns.ToDictionary(c => c.Column);
        Assert.Equal("CATO: new id on insert", columns["Id"].Source);
        Assert.Equal("Steam Store app details: header_image", columns["HeaderImageUrl"].Source);
        Assert.StartsWith("Steam Store app details: developers[0]", columns["DeveloperId"].Source);
        Assert.Contains("Unique key: AppId", columns["AppId"].Validation);

        var review = await new GetDbTableHandler(db, Stats(db), _clock).Handle(new GetDbTableQuery("steam_review"), default);
        Assert.Equal("CATO: link to main_game", review!.Columns.Single(c => c.Column == "GameId").Source);
    }

    [Fact]
    public async Task Csv_follows_the_sheet_layout()
    {
        AddCountries("TR", "DE");
        await using var db = NewContext();
        var csv = await new ExportDbTablesCsvHandler(db, Stats(db)).Handle(new ExportDbTablesCsvQuery(), default);
        var lines = csv.Split("\r\n");

        Assert.Equal("Table,Column,Data Type,Nullable,Max Length,PK,CLR Type,Record Count,Data Source,Validation,Fallback", lines[0]);
        Assert.StartsWith("action_impact,Id,", lines[1]);

        // Sheet order: entity CLR name, so main_game ("Game") sits after demo_download.
        var tableOrder = lines.Skip(1).Where(l => l.Length > 0).Select(l => l[..l.IndexOf(',')]).Distinct().ToList();
        Assert.Equal(tableOrder.IndexOf("demo_download") + 1, tableOrder.IndexOf("main_game"));

        var countryKeyRow = lines.Single(l => l.StartsWith("steam_country,CountryCode,"));
        Assert.Contains(",PK,string,2,", countryKeyRow);
    }

    [Theory]
    [InlineData(typeof(int?), "int?")]
    [InlineData(typeof(string[]), "string[]")]
    [InlineData(typeof(DateOnly), "DateOnly")]
    [InlineData(typeof(List<string>), "List<string>")]
    public void Clr_names_read_like_csharp(Type type, string expected) =>
        Assert.Equal(expected, DbTableSchemaReader.ClrName(type));

    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
