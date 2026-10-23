using System.Text.Json;
using Cato.Domain.Entities;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.Models;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.JobRuns;

public class SteamPriceWatcherTests : JobRunsDbTestBase
{
    private readonly FakeSteamApi _steam = new();

    private SteamPriceWatcherService Watcher()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        services.AddSingleton<ISteamApiService>(_steam);
        services.AddSingleton<IJobRunTracker>(sp => new JobRunTracker(
            sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobRunTracker>.Instance));
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new SteamPriceWatcherService(scopes, NullLogger<SteamPriceWatcherService>.Instance,
            Options.Create(new SteamSettings()));
    }

    private void SeedGames(params int[] appIds)
    {
        using var db = NewContext();
        foreach (var appId in appIds)
            db.Games.Add(new Game
            {
                Id = Guid.NewGuid(), AppId = appId, Name = $"Game {appId}", GameType = "Sourcing",
                IsReleased = true, ReleaseDate = new DateOnly(2024, 1, 1), PriceUsd = 9.99m,
            });
        db.SaveChanges();
    }

    private string? LastAppId()
    {
        using var db = NewContext();
        return db.WorkerStates.SingleOrDefault(s => s.Key == WorkerStateKeys.PriceWatcherLastAppId)?.Value;
    }

    private List<int> SnapshotAppIds()
    {
        using var db = NewContext();
        return db.PriceSnapshots.Join(db.Games, s => s.GameId, g => g.Id, (s, g) => g.AppId).OrderBy(a => a).ToList();
    }

    [Fact]
    public async Task A_cycle_cut_by_a_stop_keeps_what_it_read_and_the_next_cycle_goes_on_from_there()
    {
        SeedGames(10, 20, 30, 40, 50);
        using var stop = new CancellationTokenSource();
        // The app stops while the fourth price is being read.
        _steam.OnRead = appId => { if (appId == 40) stop.Cancel(); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Watcher().CheckPricesAsync(stop.Token, saveEvery: 2));

        Assert.Equal([10, 20, 30], SnapshotAppIds());
        Assert.Equal("30", LastAppId());

        _steam.Reads.Clear();
        _steam.OnRead = null;
        await Watcher().CheckPricesAsync(CancellationToken.None, saveEvery: 2);

        Assert.Equal([40, 50], _steam.Reads);
        Assert.Equal([10, 20, 30, 40, 50], SnapshotAppIds());
        Assert.Equal("0", LastAppId());
    }

    [Fact]
    public async Task Each_snapshot_carries_the_time_its_price_was_read()
    {
        SeedGames(10, 20);
        var before = DateTime.UtcNow;
        _steam.OnRead = appId => { if (appId == 20) Thread.Sleep(50); };

        await Watcher().CheckPricesAsync(CancellationToken.None, saveEvery: 1);

        using var db = NewContext();
        var times = db.PriceSnapshots.Join(db.Games, s => s.GameId, g => g.Id, (s, g) => new { g.AppId, s.CapturedAt })
            .OrderBy(x => x.AppId).Select(x => x.CapturedAt).ToList();
        Assert.All(times, t => Assert.True(t >= before));
        Assert.True(times[1] - times[0] >= TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public async Task A_cycle_reports_its_failed_reads_as_items_for_the_alert()
    {
        SeedGames(10, 20, 30);
        _steam.OnRead = appId => { if (appId == 20) throw new HttpRequestException("Steam answered 502"); };

        await Watcher().CheckPricesAsync(CancellationToken.None, saveEvery: 1);

        using var db = NewContext();
        using var metrics = JsonDocument.Parse(db.JobRuns.Single(r => r.JobName == BackendJobNames.SteamPriceWatcher).MetricsJson!);
        Assert.Equal(3, metrics.RootElement.GetProperty("items_total").GetInt64());
        Assert.Equal(1, metrics.RootElement.GetProperty("items_failed").GetInt64());
        Assert.Equal("games", metrics.RootElement.GetProperty("items_unit").GetString());
    }

    private sealed class FakeSteamApi : ISteamApiService
    {
        public List<int> Reads { get; } = [];
        public Action<int>? OnRead { get; set; }

        public Task<SteamAppData?> GetAppDetailsAsync(int appId, CancellationToken ct = default)
        {
            Reads.Add(appId);
            OnRead?.Invoke(appId);
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<SteamAppData?>(new SteamAppData
            {
                PriceOverview = new SteamPriceOverview { Currency = "USD", Initial = 999, Final = 499, DiscountPercent = 50 },
            });
        }

        public Task<List<SteamUserTag>> GetUserTagsAsync(int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamAppReviewsResponse?> GetAppReviewsAsync(int appId, string cursor = "*", CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamPlayerSummariesResponse?> GetPlayerSummariesAsync(IReadOnlyList<long> steamIds, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamSchemaForGameResponse?> GetSchemaForGameAsync(int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SteamPlayerAchievementsResponse?> GetPlayerAchievementsAsync(long steamId64, int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SteamAchievementPercent>?> GetGlobalAchievementPercentagesAsync(int appId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<List<SteamNewsItem>?> GetNewsForAppAsync(int appId, int count, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
