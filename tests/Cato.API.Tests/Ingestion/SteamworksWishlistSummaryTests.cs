using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers <see cref="GameDataService.GetSteamworksWishlistSummaryAsync"/>: lifetime totals
/// per game and a 30-day window that ends at the latest DateLocal in the table.
/// </summary>
public class SteamworksWishlistSummaryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public SteamworksWishlistSummaryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;
        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static SteamworksWishlistDaily Day(Guid gameId, string date, int adds, int deletes = 0, int purchases = 0, int gifts = 0) => new()
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        DateLocal = DateOnly.Parse(date),
        Adds = adds,
        Deletes = deletes,
        PurchasesAndActivations = purchases,
        Gifts = gifts,
    };

    [Fact]
    public async Task EmptyTableHasNoLatestDate()
    {
        await using var db = new CatoDbContext(_options);
        var summary = await new GameDataService(db).GetSteamworksWishlistSummaryAsync();

        Assert.Null(summary.LatestDate);
        Assert.Empty(summary.Games);
    }

    [Fact]
    public async Task TotalsAndThirtyDayWindowPerGame()
    {
        var chef = Guid.NewGuid();
        var rpg = Guid.NewGuid();
        await using (var db = new CatoDbContext(_options))
        {
            db.Games.AddRange(
                new Game { Id = chef, AppId = 2390230, Name = "The Chef's Shift", GameType = "Owned" },
                new Game { Id = rpg, AppId = 2429270, Name = "The RPG", GameType = "Owned" });
            db.SteamworksWishlistDaily.AddRange(
                Day(chef, "2026-09-14", 100, 10, 5, 1),   // day 31 before the latest: outside the window
                Day(chef, "2026-09-15", 50, 5, 4, 0),     // day 30: inside
                Day(chef, "2026-10-14", 20, 2, 3, 1),
                Day(rpg, "2026-10-01", 7, 1, 0, 0));
            await db.SaveChangesAsync();
        }

        await using var read = new CatoDbContext(_options);
        var summary = await new GameDataService(read).GetSteamworksWishlistSummaryAsync();

        Assert.Equal(new DateOnly(2026, 10, 14), summary.LatestDate);
        Assert.Equal([2390230, 2429270], summary.Games.Select(g => g.AppId)); // by outstanding, desc

        var game = summary.Games[0];
        Assert.Equal(new DateOnly(2026, 9, 14), game.FirstDate);
        Assert.Equal(new DateOnly(2026, 10, 14), game.LastDate);
        Assert.Equal(170, game.Adds);
        Assert.Equal(17, game.Deletes);
        Assert.Equal(12, game.PurchasesAndActivations);
        Assert.Equal(2, game.Gifts);
        Assert.Equal(170 - 17 - 12 - 2, game.Outstanding);
        Assert.Equal(70, game.Adds30d);
        Assert.Equal(7, game.Deletes30d);
        Assert.Equal(8, game.Conversions30d);
    }
}
