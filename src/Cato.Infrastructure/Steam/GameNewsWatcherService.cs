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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Game news watcher disabled via News:Enabled");
            return;
        }

        // Brief startup delay to let the database finish migrating.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
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

            await Task.Delay(TimeSpan.FromMinutes(_settings.IntervalMinutes), stoppingToken);
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
