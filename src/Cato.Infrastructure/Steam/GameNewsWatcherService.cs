using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// Keeps game_news current for Owned games from ISteamNews/GetNewsForApp (no key):
/// the newest <see cref="NewsSettings.Count"/> items per game, upserted by gid with
/// the manual news upload's mapping. Older items stay; history before the first
/// cycle is whatever the API returns.
/// </summary>
public class GameNewsWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GameNewsWatcherService> _logger;
    private readonly NewsSettings _settings;

    public GameNewsWatcherService(IServiceScopeFactory scopeFactory, ILogger<GameNewsWatcherService> logger, IOptions<NewsSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    // The first value of this watcher's scheduled_job row; the Jobs page owns it after that.
    private WatcherDefaults Defaults => new("Owned game news fetch", TimeSpan.FromMinutes(_settings.IntervalMinutes), _settings.Enabled);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay to let the database finish migrating.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        DateTime? previousEnd = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            await WatcherSchedule.WaitForNextRunAsync(
                _scopeFactory, BackendJobNames.GameNewsWatcher, Defaults, previousEnd, _logger, stoppingToken);

            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Game news cycle failed");
            }

            previousEnd = DateTime.UtcNow;
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var steamApi = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
        var tracker = scope.ServiceProvider.GetRequiredService<IJobRunTracker>();

        await using var job = await tracker.StartAsync(BackendJobNames.GameNewsWatcher, ct: ct);
        try
        {
            var games = await db.Games
                .Where(g => g.GameType == "Owned")
                .Select(g => new { g.Id, g.AppId })
                .ToListAsync(ct);

            int withNews = 0, added = 0, updated = 0, skipped = 0;
            foreach (var game in games)
            {
                if (ct.IsCancellationRequested) break;
                var items = await steamApi.GetNewsForAppAsync(game.AppId, _settings.Count, ct);
                if (items is null)
                {
                    skipped++;
                    continue;
                }
                if (items.Count == 0) continue;
                withNews++;
                var (gameAdded, gameUpdated) = await SteamPublicStatsStore.UpsertNewsAsync(db, game.Id, items, ct);
                await db.SaveChangesAsync(ct);
                added += gameAdded;
                updated += gameUpdated;
            }

            job.Set("games", games.Count);
            job.Set("withNews", withNews);
            job.Set("added", added);
            job.Set("updated", updated);
            job.Set("skipped", skipped);
            _logger.LogInformation(
                "Game news cycle complete: games={Games} withNews={WithNews} added={Added} updated={Updated} skipped={Skipped}",
                games.Count, withNews, added, updated, skipped);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }
}
