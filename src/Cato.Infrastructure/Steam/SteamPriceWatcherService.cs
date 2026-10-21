using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// Background service that periodically checks Steam Store prices for all tracked games,
/// records price snapshots, and updates base price on the Game entity.
/// </summary>
public sealed class SteamPriceWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SteamPriceWatcherService> _logger;
    private readonly SteamSettings _settings;

    public SteamPriceWatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<SteamPriceWatcherService> logger,
        IOptions<SteamSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    // The first value of this watcher's scheduled_job row; the Jobs page owns it after that.
    private WatcherDefaults Defaults => new("Store price check", TimeSpan.FromHours(_settings.PriceCheckIntervalHours), true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SteamPriceWatcher: Starting price polling every {Hours} hours", _settings.PriceCheckIntervalHours);

        // Initial delay to let other services start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        DateTime? previousEnd = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            await WatcherSchedule.WaitForNextRunAsync(
                _scopeFactory, BackendJobNames.SteamPriceWatcher, Defaults, previousEnd, _logger, stoppingToken);

            try
            {
                await CheckPricesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SteamPriceWatcher: Error during price check");
            }

            previousEnd = DateTime.UtcNow;
        }
    }

    // A cycle reads about 33,000 games behind the shared Steam throttle and takes about 18 h.
    // It saves every SaveEvery games and keeps the last saved app id in worker_state, so a
    // restart loses at most one unsaved group and the next cycle goes on from there. Until
    // 2026-10-21 it saved once at the end, and every deploy lost the whole cycle.
    internal const int SaveEvery = 200;

    internal async Task CheckPricesAsync(CancellationToken ct, int saveEvery = SaveEvery)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var steam = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
        var tracker = scope.ServiceProvider.GetRequiredService<IJobRunTracker>();

        await using var job = await tracker.StartAsync(BackendJobNames.SteamPriceWatcher, ct: ct);
        try
        {
        var stored = await WorkerStateStore.GetAsync(_scopeFactory, WorkerStateKeys.PriceWatcherLastAppId, ct);
        var resumeAfter = int.TryParse(stored, out var lastAppId) ? lastAppId : 0;
        var appIds = await db.Games.AsNoTracking()
            .Where(g => g.IsReleased)
            .Where(g => g.ReleaseDate != null && g.ReleaseDate >= new DateOnly(2023, 1, 1))
            .Where(g => g.PriceUsd != null && g.PriceUsd > 0)
            .Where(g => g.AppId > resumeAfter)
            .OrderBy(g => g.AppId)
            .Select(g => g.AppId)
            .ToListAsync(ct);

        if (resumeAfter > 0)
            _logger.LogInformation("SteamPriceWatcher: Resuming after AppId {AppId}: {Count} games left", resumeAfter, appIds.Count);
        else
            _logger.LogInformation("SteamPriceWatcher: Checking prices for {Count} games", appIds.Count);

        var counts = new PriceCounts();
        foreach (var chunk in appIds.Chunk(saveEvery))
        {
            var games = await db.Games.Where(g => chunk.Contains(g.AppId)).OrderBy(g => g.AppId).ToListAsync(ct);
            int? lastDone = null;
            try
            {
                foreach (var game in games)
                {
                    ct.ThrowIfCancellationRequested();
                    await CheckGameAsync(db, steam, game, counts, ct);
                    lastDone = game.AppId;
                }
            }
            finally
            {
                // Also when the app stops: the games read so far stay read.
                await db.SaveChangesAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                if (lastDone is { } done)
                    await WorkerStateStore.SetAsync(_scopeFactory, WorkerStateKeys.PriceWatcherLastAppId,
                        done.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
            }
        }

        // The cycle is complete: the next one starts from the first game.
        await WorkerStateStore.SetAsync(_scopeFactory, WorkerStateKeys.PriceWatcherLastAppId, "0", ct);

        job.Set("games", appIds.Count);
        job.Set("resumed_after_app_id", resumeAfter);
        job.Set("updated", counts.Updated);
        job.Set("skipped", counts.Skipped);
        job.Set("failed", counts.Failed);
        if (counts.Failed > 0) job.MarkPartialSuccess();

        _logger.LogInformation(
            "SteamPriceWatcher: Price check complete — {Updated} snapshots created, {Skipped} skipped (no price data)",
            counts.Updated, counts.Skipped);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }

    private sealed class PriceCounts
    {
        public int Updated;
        public int Skipped;
        public int Failed;
    }

    private async Task CheckGameAsync(
        CatoDbContext db, ISteamApiService steam, Game game, PriceCounts counts, CancellationToken ct)
    {
        try
        {
            var steamData = await steam.GetAppDetailsAsync(game.AppId, ct);
            if (steamData?.PriceOverview is null)
            {
                counts.Skipped++;
                return;
            }

            var price = steamData.PriceOverview;
            var basePriceUsd = price.InitialUsd ?? 0m;
            var finalPriceUsd = price.FinalUsd ?? 0m;
            var discountPercent = price.DiscountPercent ?? 0;

            // Check if price differs from the latest snapshot
            var latestSnapshot = await db.PriceSnapshots
                .Where(ps => ps.GameId == game.Id)
                .OrderByDescending(ps => ps.CapturedAt)
                .FirstOrDefaultAsync(ct);

            var priceChanged = latestSnapshot is null
                || latestSnapshot.BasePriceUsd != basePriceUsd
                || latestSnapshot.FinalPriceUsd != finalPriceUsd
                || latestSnapshot.DiscountPercent != discountPercent;

            if (priceChanged)
            {
                db.PriceSnapshots.Add(new PriceSnapshot
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    // When this price was read, not when the 18 h cycle began.
                    CapturedAt = DateTime.UtcNow,
                    BasePriceUsd = basePriceUsd,
                    FinalPriceUsd = finalPriceUsd,
                    DiscountPercent = discountPercent,
                    Currency = price.Currency ?? "USD"
                });

                counts.Updated++;
            }

            // Always update game with fresh base price and discount
            game.PriceUsd = basePriceUsd;
            game.DiscountPercent = discountPercent;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            counts.Failed++;
            _logger.LogWarning(ex, "SteamPriceWatcher: Failed to check price for AppId {AppId}", game.AppId);
        }
    }
}
