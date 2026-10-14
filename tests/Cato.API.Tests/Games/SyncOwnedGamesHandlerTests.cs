using System.Collections.Concurrent;
using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using Cato.API.Services.Handlers.Games;
using Cato.API.Validators.Games;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Games;

/// <summary>
/// Covers <see cref="SyncOwnedGamesHandler"/>: every app on the publisher account
/// ends up as an Owned row before the wishlist batch arrives, and only games that
/// were never enriched are queued for Steam enrichment.
/// </summary>
public class SyncOwnedGamesHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;

    public SyncOwnedGamesHandlerTests()
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

    private CatoDbContext NewContext() => new(_options);

    private async Task SeedAsync(int appId, string gameType, bool enriched)
    {
        await using var db = NewContext();
        db.Games.Add(new Game
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            Name = $"Seeded {appId}",
            GameType = gameType,
            LastEnrichedAt = enriched ? DateTime.UtcNow : null
        });
        await db.SaveChangesAsync();
    }

    private async Task<(SyncOwnedGamesResult Result, RecordingRedisSync Redis, RecordingEnricher Enricher)>
        SyncAsync(params OwnedGameEntry[] games)
    {
        var redis = new RecordingRedisSync();
        var enricher = new RecordingEnricher();
        var scopeFactory = new ServiceCollection()
            .AddScoped<IGameService>(_ => enricher)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        await using var db = NewContext();
        // UpsertGameAsync touches only the database and Redis; the Steam clients stay unused.
        var gameService = new GameService(db, null!, null!, redis);
        var handler = new SyncOwnedGamesHandler(db, gameService, scopeFactory, NullLogger<SyncOwnedGamesHandler>.Instance);

        var result = await handler.Handle(new SyncOwnedGamesCommand(games), CancellationToken.None);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return (result.Data!, redis, enricher);
    }

    private async Task<Game> GameAsync(int appId)
    {
        await using var db = NewContext();
        return await db.Games.SingleAsync(g => g.AppId == appId);
    }

    [Fact]
    public async Task CreatesUnknownAppsAsOwnedAndQueuesEnrichment()
    {
        var (result, redis, enricher) = await SyncAsync(new OwnedGameEntry(2390230, "The Chef's Shift"));

        Assert.Equal(new SyncOwnedGamesResult(1, 0, 0, 0, 1), result);
        var game = await GameAsync(2390230);
        Assert.Equal("Owned", game.GameType);
        Assert.Equal("The Chef's Shift", game.Name);
        Assert.Equal([(2390230, "Owned")], redis.Synced);
        Assert.Equal([game.Id], await enricher.WaitForAsync(1));
    }

    [Fact]
    public async Task PromotesAnExistingNonOwnedGame()
    {
        await SeedAsync(2429270, "Sourcing", enriched: true);

        var (result, redis, _) = await SyncAsync(new OwnedGameEntry(2429270, "The RPG"));

        Assert.Equal(new SyncOwnedGamesResult(0, 1, 0, 0, 0), result);
        var game = await GameAsync(2429270);
        Assert.Equal("Owned", game.GameType);
        // The name stays the enriched store name, not the partner list name.
        Assert.Equal("Seeded 2429270", game.Name);
        Assert.Equal([(2429270, "Sourcing", "Owned")], redis.Updated);
    }

    [Fact]
    public async Task LeavesOwnedGamesAloneAndOnlyQueuesTheNeverEnriched()
    {
        await SeedAsync(1, "Owned", enriched: true);
        await SeedAsync(2, "Owned", enriched: false);

        var (result, redis, enricher) = await SyncAsync(
            new OwnedGameEntry(1, null), new OwnedGameEntry(2, null));

        Assert.Equal(new SyncOwnedGamesResult(0, 0, 2, 0, 1), result);
        Assert.Empty(redis.Synced);
        Assert.Empty(redis.Updated);
        Assert.Equal([(await GameAsync(2)).Id], await enricher.WaitForAsync(1));
    }

    [Fact]
    public async Task RepeatedAppIdIsHandledOnce()
    {
        var (result, _, _) = await SyncAsync(
            new OwnedGameEntry(7, "First"), new OwnedGameEntry(7, "Second"));

        Assert.Equal(1, result.Created);
        Assert.Equal("Second", (await GameAsync(7)).Name);
    }

    [Fact]
    public void ValidatorRejectsAMissingOrBadListWithoutThrowing()
    {
        var validator = new SyncOwnedGamesValidator();

        Assert.False(validator.Validate(new SyncOwnedGamesCommand(null!)).IsValid);
        Assert.False(validator.Validate(new SyncOwnedGamesCommand([])).IsValid);
        Assert.False(validator.Validate(new SyncOwnedGamesCommand([new OwnedGameEntry(0, null)])).IsValid);
        Assert.True(validator.Validate(new SyncOwnedGamesCommand([new OwnedGameEntry(2390230, "x")])).IsValid);
    }

    private sealed class RecordingRedisSync : IRedisAppIdSyncService
    {
        public List<(int AppId, string GameType)> Synced { get; } = [];
        public List<(int AppId, string OldType, string NewType)> Updated { get; } = [];

        public Task SyncAsync(int appId, string gameType, string? name, CancellationToken ct)
        {
            Synced.Add((appId, gameType));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(int appId, string oldType, string newType, string? name, CancellationToken ct)
        {
            Updated.Add((appId, oldType, newType));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(int appId, CancellationToken ct) => Task.CompletedTask;

        public Task PrioritizeFollowerHistoryAsync(int appId, DateTimeOffset analyzedAt, CancellationToken ct)
            => Task.CompletedTask;
    }

    /// <summary>Stands in for the background scope's IGameService; records enrichment only.</summary>
    private sealed class RecordingEnricher : IGameService
    {
        private readonly ConcurrentQueue<Guid> _enriched = new();

        public async Task<List<Guid>> WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_enriched.Count < count && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            return [.. _enriched];
        }

        public Task<Result<GameDto>> EnrichGameFromSteamAsync(Guid id, CancellationToken ct = default)
        {
            _enriched.Enqueue(id);
            return Task.FromResult(Result<GameDto>.Failure("not enriched in tests"));
        }

        public Task<Result<GameDto>> CreateGameAsync(CreateGameCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<UpsertGameResult>> UpsertGameAsync(CreateGameCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<GameDto>> ListGamesAsync(ListGamesQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<GameDto>> CatalogGamesAsync(CatalogGamesQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<GameDto>> GetGameDetailsAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<GameDto>> UpdateGameAsync(UpdateGameCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeleteGameAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
