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
/// Special events and sale collection share one handler. A sale event lists hundreds of games,
/// so it looks games and links up per event and forgets each saved event.
/// </summary>
public class SpecialEventsIngestionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public SpecialEventsIngestionTests()
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

    private const string Payload = """
        {
          "hub_tabs": [{"unique_id": 7, "label": "Top sellers"}],
          "events": [
            {"announcement_gid": "e1", "title": "Spooky sale", "hub_placements": [7],
             "games": [
               {"app_id": 10, "item_type": "game", "discount_percent": 50},
               {"app_id": 11, "item_type": "game", "name": "New Game"},
               {"app_id": 11, "item_type": "game", "name": "New Game"},
               {"app_id": 12, "item_type": "bundle"}
             ]},
            {"announcement_gid": "e2", "title": "Demo fest",
             "games": [
               {"app_id": 11, "item_type": "game"},
               {"app_id": 13, "item_type": "demo", "name": "A Demo"}
             ]},
            {"title": "no gid"}
          ]
        }
        """;

    private async Task<IngestionResult> IngestAsync(CatoDbContext db) =>
        await new IngestionService(db, NullLogger<IngestionService>.Instance).IngestSpecialEventsAsync(
            new IngestSpecialEventsCommand(JsonDocument.Parse(Payload).RootElement, "steam_sale_collection"));

    [Fact]
    public async Task Games_and_links_are_stubbed_updated_and_the_tracker_is_left_empty()
    {
        var existingGame = new Game { Id = Guid.NewGuid(), AppId = 10, Name = "Known", GameType = "Owned" };
        var existingEvent = new SteamSpecialEvent { Id = Guid.NewGuid(), AnnouncementGid = "e1", FirstSeenAt = DateTime.UtcNow.AddDays(-3) };
        using (var seed = new CatoDbContext(_options))
        {
            seed.Games.Add(existingGame);
            seed.SteamSpecialEvents.Add(existingEvent);
            seed.SteamSpecialEventGames.Add(new SteamSpecialEventGame
            {
                Id = Guid.NewGuid(), SteamSpecialEventId = existingEvent.Id, GameId = existingGame.Id,
                DiscountPercent = 20, FirstSeenAt = existingEvent.FirstSeenAt, LastSeenAt = existingEvent.FirstSeenAt,
            });
            seed.SaveChanges();
        }

        await using var db = new CatoDbContext(_options);
        var result = await IngestAsync(db);

        // e1 and its link to app 10 existed; e2 and the links to 11 (twice) and 13 are new.
        Assert.Equal(2, result.RecordsProcessed);
        Assert.Equal(4, result.RecordsInserted);
        Assert.Equal(2, result.RecordsUpdated);
        Assert.Equal(1, result.RecordsFailed);
        Assert.True(db.ChangeTracker.Entries().Count() <= 1, "only the ingestion log may stay tracked");

        await using var check = new CatoDbContext(_options);
        var games = check.Games.ToDictionary(g => g.AppId);
        Assert.Equal([10, 11, 13], games.Keys.Order());
        Assert.Equal("New Game", games[11].Name);
        Assert.Equal("Other", games[11].GameType);
        Assert.Equal("Owned", games[10].GameType);

        var links = check.SteamSpecialEventGames.Include(l => l.Game).Include(l => l.SteamSpecialEvent).ToList()
            .ToDictionary(l => (l.SteamSpecialEvent.AnnouncementGid, l.Game.AppId));
        Assert.Equal(4, links.Count);
        Assert.Equal(50, links[("e1", 10)].DiscountPercent);
        Assert.Equal(existingEvent.FirstSeenAt, links[("e1", 10)].FirstSeenAt);
        Assert.Equal("demo", links[("e2", 13)].ItemType);

        // The same message again (a redelivery) adds nothing.
        await using var again = new CatoDbContext(_options);
        var second = await IngestAsync(again);
        Assert.Equal(0, second.RecordsInserted);
        Assert.Equal(6, second.RecordsUpdated);
        await using var after = new CatoDbContext(_options);
        Assert.Equal(3, after.Games.Count());
        Assert.Equal(4, after.SteamSpecialEventGames.Count());
    }
}
