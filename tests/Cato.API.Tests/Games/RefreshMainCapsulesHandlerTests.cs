using Cato.API.Models.Games;
using Cato.API.Services.Handlers.Games;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.Models;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cato.API.Tests.Games;

/// <summary>
/// Covers <see cref="RefreshMainCapsulesHandler"/>: only the capsule URL changes, in
/// batches of <see cref="SteamApiService.MaxCapsuleBatch"/>, and a failed batch skips only its games.
/// </summary>
public class RefreshMainCapsulesHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CatoDbContext> _options;
    private readonly FakeSteamApi _steam = new();
    private readonly FakeCache _cache = new();

    public RefreshMainCapsulesHandlerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CatoDbContext>().UseSqlite(_connection).Options;
        using var db = new CatoDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private static string Small(int appId) => $"https://cdn.example/{appId}/capsule_231x87.jpg";
    private static string Main(int appId) => $"https://cdn.example/{appId}/capsule_616x353.jpg";

    private async Task SeedAsync(params int[] appIds)
    {
        await using var db = new CatoDbContext(_options);
        foreach (var appId in appIds)
            db.Games.Add(new Game { Id = Guid.NewGuid(), AppId = appId, Name = $"Game {appId}", GameType = "Other", CapsuleImageUrl = Small(appId) });
        await db.SaveChangesAsync();
    }

    private async Task<RefreshMainCapsulesResult> RunAsync(params int[] appIds)
    {
        await using var db = new CatoDbContext(_options);
        var handler = new RefreshMainCapsulesHandler(db, _steam, _cache, NullLogger<RefreshMainCapsulesHandler>.Instance);
        var result = await handler.Handle(new RefreshMainCapsulesCommand([.. appIds]), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Data!;
    }

    private async Task<string?> CapsuleAsync(int appId)
    {
        await using var db = new CatoDbContext(_options);
        return (await db.Games.SingleAsync(g => g.AppId == appId)).CapsuleImageUrl;
    }

    [Fact]
    public async Task Known_games_get_the_main_capsule_and_others_keep_theirs()
    {
        await SeedAsync(1, 2);
        _steam.Capsules[1] = Main(1); // app 2: Steam has no main capsule

        var result = await RunAsync(1, 2, 3, 1); // 3: unknown to CATO; 1 twice

        Assert.Equal(new RefreshMainCapsulesResult(3, 2, 1, 0), result);
        Assert.Equal(Main(1), await CapsuleAsync(1));
        Assert.Equal(Small(2), await CapsuleAsync(2));
        Assert.Equal(1, _cache.Evictions);
    }

    [Fact]
    public async Task A_failed_batch_skips_only_its_games()
    {
        var appIds = Enumerable.Range(1, SteamApiService.MaxCapsuleBatch + 1).ToArray();
        await SeedAsync(appIds);
        foreach (var id in appIds) _steam.Capsules[id] = Main(id);
        _steam.FailCall = 1; // the first batch of 100

        var result = await RunAsync(appIds);

        Assert.Equal(1, result.FailedBatches);
        Assert.Equal(1, result.Updated);
        Assert.Equal(new[] { SteamApiService.MaxCapsuleBatch, 1 }, _steam.BatchSizes);
    }

    [Fact]
    public async Task Too_many_app_ids_are_refused()
    {
        await using var db = new CatoDbContext(_options);
        var handler = new RefreshMainCapsulesHandler(db, _steam, _cache, NullLogger<RefreshMainCapsulesHandler>.Instance);
        var ids = Enumerable.Range(1, RefreshMainCapsulesCommand.MaxAppIds + 1).ToList();

        var result = await handler.Handle(new RefreshMainCapsulesCommand(ids), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(_steam.BatchSizes);
    }

    private sealed class FakeCache : IOutputCacheStore
    {
        public int Evictions { get; private set; }
        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) { Evictions++; return ValueTask.CompletedTask; }
        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);
        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FakeSteamApi : ISteamApiService
    {
        public Dictionary<int, string> Capsules { get; } = [];
        public List<int> BatchSizes { get; } = [];
        public int? FailCall { get; set; }

        public Task<IReadOnlyDictionary<int, string>> GetMainCapsuleUrlsAsync(IReadOnlyList<int> appIds, CancellationToken ct = default)
        {
            BatchSizes.Add(appIds.Count);
            if (FailCall == BatchSizes.Count) throw new HttpRequestException("GetItems answered 500.");
            return Task.FromResult<IReadOnlyDictionary<int, string>>(
                appIds.Where(Capsules.ContainsKey).ToDictionary(id => id, id => Capsules[id]));
        }

        public Task<string?> GetMainCapsuleUrlAsync(int appId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamAppData?> GetAppDetailsAsync(int appId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<int, SteamPriceOverview>> GetPriceOverviewsAsync(IReadOnlyList<int> appIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<SteamUserTag>> GetUserTagsAsync(int appId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamAppReviewsResponse?> GetAppReviewsAsync(int appId, string cursor = "*", CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamPlayerSummariesResponse?> GetPlayerSummariesAsync(IReadOnlyList<long> steamIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamSchemaForGameResponse?> GetSchemaForGameAsync(int appId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SteamPlayerAchievementsResponse?> GetPlayerAchievementsAsync(long steamId64, int appId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<SteamAchievementPercent>?> GetGlobalAchievementPercentagesAsync(int appId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<SteamNewsItem>?> GetNewsForAppAsync(int appId, int count, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
