using Cato.API.Services.JobRuns;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.JobRuns;

/// <summary>SQLite in-memory database plus a pinned clock and API start time.</summary>
public abstract class JobRunsDbTestBase : IDisposable
{
    protected static readonly DateTime Now = new(2026, 10, 6, 15, 21, 0, DateTimeKind.Utc);
    protected static readonly DateTime ApiStart = new(2026, 10, 6, 14, 17, 40, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    protected JobRunsDbTestBase()
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

    protected CatoDbContext NewContext() => new(_options);

    protected static TimeProvider Clock { get; } = new FixedTimeProvider(Now);

    protected static ApiProcessInfo Process { get; } = new(ApiStart);

    protected static JobCatalog NewCatalog(CatoDbContext db, string webApiKey = "key") => new(
        db,
        Options.Create(new SteamSettings()),
        Options.Create(new GameEnrichmentSettings()),
        Options.Create(new AchievementSettings()),
        Options.Create(new PlayerProfileSettings()),
        Options.Create(new SteamWebApiSettings { ApiKey = webApiKey }));

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
