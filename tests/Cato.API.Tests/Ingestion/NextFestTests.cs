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
/// Covers the <c>steamworks_nextfest</c> item (one game's cumulative snapshots during one
/// fest) and the fest query. Figures are from The RPG (2429270, demo 3221750) in the
/// June 2025 fest.
/// </summary>
public class NextFestTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public NextFestTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CatoDbContext NewContext() => new(_options);

    private static object Row(long end, int players, int wishlists, int playerWishlists) => new
    {
        rt_start_time = 1749488400L, rt_end_time = end, demo_player_count = players, wishlist_count = wishlists,
        player_wishlist_count = playerWishlists, fest_page_views = (long?)null, fest_period_page_views = (long?)null,
    };

    private static JsonElement Item(string festKey, params object[] rows) => JsonSerializer.SerializeToElement(new
    {
        fest_key = festKey, fest_slug = festKey["sale_nextfest_".Length..], event_start = 1749488400L, event_end = 1750093200L,
        demo_app_id = 3221750, rows,
    });

    private async Task<ItemIngestResult> IngestAsync(int appId, JsonElement item)
    {
        await using var db = NewContext();
        var result = await new IngestionService(db, NullLogger<IngestionService>.Instance)
            .IngestSteamworksNextFestItemAsync(appId, DateTimeOffset.UtcNow, item);
        await db.SaveChangesAsync();
        return result;
    }

    [Fact]
    public async Task SnapshotsAreKeyedByEndAndReReadingChangesNothing()
    {
        await using (var seed = NewContext())
        {
            seed.Games.Add(new Game { Id = Guid.NewGuid(), AppId = 2429270, Name = "The RPG", GameType = "Owned" });
            await seed.SaveChangesAsync();
        }
        var first = await IngestAsync(2429270, Item("sale_nextfest_june_2025", Row(1749574800, 300, 500, 100), Row(1750093200, 1744, 2264, 673)));
        var same = await IngestAsync(2429270, Item("sale_nextfest_june_2025", Row(1749574800, 300, 500, 100), Row(1750093200, 1744, 2264, 673)));
        var corrected = await IngestAsync(2429270, Item("sale_nextfest_june_2025", Row(1750093200, 1745, 2264, 673), new { rt_end_time = "x" }));

        Assert.Equal((2, 0, 0), (first.Inserted, first.Updated, first.Failed));
        Assert.Equal((0, 0), (same.Inserted, same.Updated));
        Assert.Equal((0, 1, 1), (corrected.Inserted, corrected.Updated, corrected.Failed));
        await using var db = NewContext();
        var last = await db.SteamworksNextFestStats.SingleAsync(s => s.SnapshotEnd == DateTimeOffset.FromUnixTimeSeconds(1750093200).UtcDateTime);
        Assert.Equal((1745, 3221750, DateTimeOffset.FromUnixTimeSeconds(1749488400).UtcDateTime), (last.DemoPlayerCount, last.DemoAppId!.Value, last.EventStart!.Value));
    }

    [Fact]
    public async Task QueryListsFestsNewestFirstWithFinalNumbers()
    {
        await IngestAsync(2429270, Item("sale_nextfest_june_2025", Row(1749574800, 300, 500, 100), Row(1750093200, 1744, 2264, 673)));
        await IngestAsync(3297110, Item("sale_nextfest_june_2025", Row(1750093200, 577, 2822, 201)));
        await IngestAsync(2390230, Item("sale_nextfest_feb_2025", Row(1741024800, 900, 1500, 300)));

        await using var db = NewContext();
        var dto = await new GameDataService(db).GetNextFestAsync();

        Assert.Equal(["june_2025", "feb_2025"], dto.Fests.Select(f => f.Slug).ToList());
        var june = dto.Fests[0];
        Assert.Equal([3297110, 2429270], june.Games.Select(g => g.AppId).ToList()); // most wishlists first
        var rpg = june.Games[1];
        Assert.Equal((1744, 2264, 673, 2), (rpg.DemoPlayers, rpg.Wishlists, rpg.PlayerWishlists, rpg.Days.Count));
        Assert.True(rpg.Days[0].SnapshotEnd < rpg.Days[1].SnapshotEnd);
    }
}
