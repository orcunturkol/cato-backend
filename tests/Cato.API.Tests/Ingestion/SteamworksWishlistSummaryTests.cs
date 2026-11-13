using Cato.API.Models.Ingestion;
using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Ingestion;

/// <summary>
/// Covers <see cref="GameDataService.GetSteamworksWishlistSummaryAsync"/>: lifetime totals
/// per game and a 30-day window that ends at the latest DateLocal in the table, and the
/// lifetime sums per partner over steamworks_partner_app.
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
        Assert.Empty(summary.Partners);
        Assert.Equal(new SteamworksWishlistTotalsDto(0, 0, 0, 0, 0, 0), summary.AllPartners);
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

    [Fact]
    public async Task LifetimeSumsPerPartnerOverItsApps()
    {
        var chef = Guid.NewGuid();
        var rpg = Guid.NewGuid();
        var pile = Guid.NewGuid();
        var now = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        await using (var db = new CatoDbContext(_options))
        {
            db.Games.AddRange(
                new Game { Id = chef, AppId = 2390230, Name = "The Chef's Shift", GameType = "Owned" },
                new Game { Id = rpg, AppId = 2429270, Name = "The RPG", GameType = "Owned" },
                new Game { Id = pile, AppId = 2632680, Name = "Pile Up!", GameType = "Owned" });
            db.SteamworksWishlistDaily.AddRange(
                Day(chef, "2026-10-01", 100, 10, 5, 1),
                Day(chef, "2026-10-02", 50, 5, 4, 0),
                Day(rpg, "2026-10-01", 40, 4, 2, 1),
                Day(pile, "2026-10-02", 1000, 100, 50, 10));
            db.SteamworksPartners.AddRange(
                new SteamworksPartner { PartnerId = 337008, Name = "PLANZET - FZCO", Enabled = true, DiscoveredAt = now },
                new SteamworksPartner { PartnerId = 216530, Name = "PLAN B YAZILIM", Enabled = true, DiscoveredAt = now },
                new SteamworksPartner { PartnerId = 356969, Name = "Bekir", Enabled = true, DiscoveredAt = now });
            db.SteamworksPartnerApps.AddRange(
                new SteamworksPartnerApp { PartnerId = 216530, AppId = 2390230, LastSeenAt = now },
                new SteamworksPartnerApp { PartnerId = 216530, AppId = 2429270, LastSeenAt = now },
                new SteamworksPartnerApp { PartnerId = 216530, AppId = 999999, LastSeenAt = now }, // listed, no data yet
                new SteamworksPartnerApp { PartnerId = 337008, AppId = 2632680, LastSeenAt = now },
                new SteamworksPartnerApp { PartnerId = 337008, AppId = 2429270, LastSeenAt = now }); // also PLAN B's
            await db.SaveChangesAsync();
        }

        await using var read = new CatoDbContext(_options);
        var summary = await new GameDataService(read).GetSteamworksWishlistSummaryAsync();

        Assert.Equal(3, summary.Games.Count);
        // Equivalent, not Equal: the records hold lists, which compare by reference.
        Assert.Equivalent(
            new[]
            {
                new SteamworksWishlistPartnerSummaryDto(216530, "PLAN B YAZILIM", 3, 190, 19, 11, 2, 190 - 19 - 11 - 2, [999999, 2390230, 2429270]),
                new SteamworksWishlistPartnerSummaryDto(337008, "PLANZET - FZCO", 2, 1040, 104, 52, 11, 1040 - 104 - 52 - 11, [2429270, 2632680]),
                new SteamworksWishlistPartnerSummaryDto(356969, "Bekir", 0, 0, 0, 0, 0, 0, []),
            },
            summary.Partners,
            strict: true);
        Assert.Equal([999999, 2390230, 2429270], summary.Partners[0].AppIds);
        // The RPG is listed by both partners and counts once.
        Assert.Equal(new SteamworksWishlistTotalsDto(4, 1190, 119, 61, 12, 1190 - 119 - 61 - 12), summary.AllPartners);
    }
}
