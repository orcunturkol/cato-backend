using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// Fills in Steam store data — genres, tags, release date, developer, media, price —
/// for games that carry none.
///
/// Games arrive in CATO from several directions and only some of them get enriched on
/// the way in. A <c>game.analyzed</c> event enriches inline, but only when Steam
/// answers at that moment; a PICS discovery enriches on its own; a Redis backfill or a
/// batch ingestion creates nothing but an app ID and a name. This sweep is what makes
/// "every game eventually has its store record" true regardless of the door it came
/// through, and it is the retry path for every inline attempt that failed.
///
/// Reddit-analysed games are served first: they are the ones someone is actively
/// asking questions about, and there are three orders of magnitude fewer of them than
/// the discovered population behind them in the queue.
/// </summary>
public sealed class GameEnrichmentWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GameEnrichmentWatcherService> _logger;
    private readonly GameEnrichmentSettings _settings;

    public GameEnrichmentWatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<GameEnrichmentWatcherService> logger,
        IOptions<GameEnrichmentSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("GameEnrichmentWatcher: disabled by configuration");
            return;
        }

        _logger.LogInformation(
            "GameEnrichmentWatcher: starting — every {Minutes} min, {BatchSize} games per cycle",
            _settings.IntervalMinutes, _settings.BatchSize);

        // Let the rest of the host settle, and stagger against the other Steam
        // watchers so a restart does not put four of them on the throttle at once.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GameEnrichmentWatcher: cycle failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_settings.IntervalMinutes), stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var enrichment = scope.ServiceProvider.GetRequiredService<ISteamGameEnrichmentService>();
        var tracker = scope.ServiceProvider.GetRequiredService<IJobRunTracker>();

        await using var job = await tracker.StartAsync(BackendJobNames.GameEnrichmentWatcher, ct: ct);
        try
        {
            var batch = await GameEnrichmentQueue.SelectAsync(db, _settings, ct);

            if (batch.GameIds.Count == 0)
            {
                job.Set("selected", 0);
                _logger.LogInformation("GameEnrichmentWatcher: nothing to enrich");
                return;
            }

            _logger.LogInformation(
                "GameEnrichmentWatcher: enriching {Count} games ({Analysed} reddit-analysed, {Refresh} refreshes)",
                batch.GameIds.Count, batch.Analysed, batch.Refresh);

            var enriched = 0;
            var failed = 0;

            foreach (var gameId in batch.GameIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    if (await enrichment.EnrichGameAsync(gameId, ct))
                        enriched++;
                    else
                        failed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    _logger.LogWarning(ex, "GameEnrichmentWatcher: failed to enrich game {GameId}", gameId);
                }
            }

            job.Set("selected", batch.GameIds.Count);
            job.Set("analysed", batch.Analysed);
            job.Set("refreshed", batch.Refresh);
            job.Set("enriched", enriched);
            job.Set("failed", failed);
            if (failed > 0) job.MarkPartialSuccess();

            _logger.LogInformation(
                "GameEnrichmentWatcher: cycle complete — {Enriched} enriched, {Failed} failed of {Total}",
                enriched, failed, batch.GameIds.Count);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }
}
