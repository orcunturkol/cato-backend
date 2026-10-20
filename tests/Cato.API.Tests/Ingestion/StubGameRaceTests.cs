using System.Text.Json;
using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// The queues run concurrently, so two of them can stub the same unknown app at once.
/// The loser must reuse the winner's row, not fail its whole batch on the unique index.
/// </summary>
public class StubGameRaceTests : IDisposable
{
    private const int AppId = 2390230;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _plain;

    public StubGameRaceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _plain = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;
        using var db = new CatoDbContext(_plain);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_second_stub_of_one_app_reuses_the_first()
    {
        var winner = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<CatoDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new OtherQueueStubsFirst(_plain, winner))
            .Options;
        await using var db = new CatoDbContext(options);
        var data = JsonDocument.Parse("""{"group_member_count": 4021}""").RootElement;

        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestGroupMemberCountItemAsync(AppId, DateTimeOffset.UtcNow, data);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.Inserted);
        await using var check = new CatoDbContext(_plain);
        var game = Assert.Single(check.Games);
        Assert.Equal(winner, game.Id);
        Assert.Equal(winner, check.GroupMemberCountSnapshots.Single().GameId);
    }

    /// <summary>Just before the stub is saved, another context inserts the same app id.</summary>
    private sealed class OtherQueueStubsFirst(DbContextOptions<CatoDbContext> options, Guid winner)
        : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!_done && eventData.Context!.ChangeTracker.Entries<Game>().Any(e => e.State == EntityState.Added))
            {
                _done = true;
                await using var other = new CatoDbContext(options);
                other.Games.Add(new Game { Id = winner, AppId = AppId, Name = $"App {AppId}", GameType = "Other" });
                await other.SaveChangesAsync(ct);
            }
            return result;
        }
    }
}
