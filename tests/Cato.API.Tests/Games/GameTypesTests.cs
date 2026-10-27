using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Games;

/// <summary>
/// Which game types the collectors track (reviews, CCU, group scrape, achievements):
/// Sourcing, Owned and OGT, the type OGT gives a game while its team evaluates it.
/// </summary>
public class GameTypesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public GameTypesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;
        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Theory]
    [InlineData("Sourcing", true)]
    [InlineData("Owned", true)]
    [InlineData("OGT", true)]
    [InlineData("Competitor", false)]
    [InlineData("Other", false)]
    public void Collectors_track_sourcing_owned_and_ogt(string gameType, bool tracked)
    {
        Assert.Equal(tracked, GameTypes.IsTracked(gameType));
        Assert.Equal(tracked, RedisAppIdSyncService.ShouldTrack(gameType));
    }

    [Fact]
    public async Task The_tracked_filter_runs_in_the_database()
    {
        await using (var db = new CatoDbContext(_options))
        {
            foreach (var (appId, type) in new[] { (1, "Owned"), (2, "Sourcing"), (3, "OGT"), (4, "Other"), (5, "Competitor") })
                db.Games.Add(new Game { Id = Guid.NewGuid(), AppId = appId, Name = $"Game {appId}", GameType = type });
            await db.SaveChangesAsync();
        }

        await using var read = new CatoDbContext(_options);
        var tracked = await read.Games
            .Where(g => GameTypes.Tracked.Contains(g.GameType))
            .Select(g => g.AppId)
            .ToListAsync();

        Assert.Equal([1, 2, 3], tracked.Order());
    }
}
