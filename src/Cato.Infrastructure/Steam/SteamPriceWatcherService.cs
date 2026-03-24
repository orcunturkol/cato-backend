using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(_settings.PriceCheckIntervalHours);
        _logger.LogInformation("SteamPriceWatcher: Starting price polling every {Hours} hours", _settings.PriceCheckIntervalHours);

        // Initial delay to let other services start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
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

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task CheckPricesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var steam = scope.ServiceProvider.GetRequiredService<ISteamApiService>();

        var games = await db.Games
            .Where(g => g.GameType != "Sourcing")
            .OrderBy(g => g.AppId)
            .ToListAsync(ct);

        _logger.LogInformation("SteamPriceWatcher: Checking prices for {Count} games", games.Count);

        var now = DateTime.UtcNow;
        int updated = 0;
        int skipped = 0;

        foreach (var game in games)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var steamData = await steam.GetAppDetailsAsync(game.AppId, ct);
                if (steamData?.PriceOverview is null)
                {
                    skipped++;
                    continue;
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
                        CapturedAt = now,
                        BasePriceUsd = basePriceUsd,
                        FinalPriceUsd = finalPriceUsd,
                        DiscountPercent = discountPercent,
                        Currency = price.Currency ?? "USD"
                    });

                    updated++;
                }

                // Always update game with fresh base price and discount
                game.PriceUsd = basePriceUsd;
                game.DiscountPercent = discountPercent;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SteamPriceWatcher: Failed to check price for AppId {AppId}", game.AppId);
            }
        }

        if (updated > 0 || games.Count > 0)
            await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SteamPriceWatcher: Price check complete — {Updated} snapshots created, {Skipped} skipped (no price data)",
            updated, skipped);
    }
}
