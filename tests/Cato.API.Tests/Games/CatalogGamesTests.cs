using Cato.API.Models.Games;
using Cato.API.Services;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Tests.Games;

/// <summary>
/// Covers <see cref="GameService.CatalogGamesAsync"/>: window browsing (OGT's "new pages")
/// shows only games with a store record, while appId lookups return every row.
/// </summary>
public class CatalogGamesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public CatalogGamesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private async Task SeedAsync(int appId, bool enriched, bool filtered = false)
    {
        await using var db = new CatoDbContext(_options);
        db.Games.Add(new Game
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            Name = enriched ? $"Game {appId}" : $"App {appId}",
            GameType = "Other",
            LastEnrichedAt = enriched ? DateTime.UtcNow : null,
            IsFiltered = filtered
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<int>> CatalogAppIdsAsync(CatalogGamesQuery query)
    {
        await using var db = new CatoDbContext(_options);
        var service = new GameService(db, null!, null!, null!);
        var page = await service.CatalogGamesAsync(query);
        return page.Items.Select(g => g.AppId).Order().ToList();
    }

    [Fact]
    public async Task Window_browsing_hides_stubs_and_filtered_games()
    {
        await SeedAsync(100, enriched: true);
        await SeedAsync(200, enriched: false);
        await SeedAsync(300, enriched: true, filtered: true);

        Assert.Equal([100], await CatalogAppIdsAsync(new CatalogGamesQuery()));
    }

    [Fact]
    public async Task An_app_id_lookup_returns_stubs_too()
    {
        await SeedAsync(100, enriched: true);
        await SeedAsync(200, enriched: false);

        Assert.Equal([100, 200], await CatalogAppIdsAsync(new CatalogGamesQuery(AppIds: [100, 200])));
    }
}
