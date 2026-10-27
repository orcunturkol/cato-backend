using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// Keeps the review totals of the listed games (<see cref="IReviewTotalList"/>) that CATO does not
/// sync in full: one first-page read per game every ReviewTotalsRefreshDays, at most
/// ReviewTotalsBatchSize games a cycle, through the same GetAppReviews call and throttle as the
/// daily review sync. Only the summary is stored (a review_summary_snapshot row, Game.ReviewCount
/// and Game.SteamReviewScore), never the reviews. See <see cref="ReviewTotals"/>.
/// </summary>
public class ReviewTotalWatcherService : BackgroundService
{
    // A game Steam gave no summary for waits a day, so it does not take a slot of every cycle.
    // Kept in memory: after a restart those games are tried once more.
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReviewTotalWatcherService> _logger;
    private readonly SteamSettings _settings;
    private readonly Dictionary<int, DateTime> _failedAt = new();

    public ReviewTotalWatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReviewTotalWatcherService> logger,
        IOptions<SteamSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    // The first value of this watcher's scheduled_job row; the Jobs page owns it after that.
    private WatcherDefaults Defaults =>
        new("Review totals of listed games", TimeSpan.FromMinutes(_settings.ReviewTotalsIntervalMinutes), true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay to let the database finish migrating.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        DateTime? previousEnd = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            await WatcherSchedule.WaitForNextRunAsync(
                _scopeFactory, BackendJobNames.ReviewTotalWatcher, Defaults, previousEnd, _logger, stoppingToken);

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
                _logger.LogError(ex, "Review total cycle failed");
            }

            previousEnd = DateTime.UtcNow;
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var steamApi = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
        var list = scope.ServiceProvider.GetRequiredService<IReviewTotalList>();
        var tracker = scope.ServiceProvider.GetRequiredService<IJobRunTracker>();

        await using var job = await tracker.StartAsync(BackendJobNames.ReviewTotalWatcher, ct: ct);
        try
        {
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            foreach (var appId in _failedAt.Where(f => now - f.Value >= FailureBackoff).Select(f => f.Key).ToList())
                _failedAt.Remove(appId);

            var appIds = await list.GetAsync(ct);
            var batch = await ReviewTotals.SelectAsync(db, appIds, _settings, today, _failedAt.Keys.ToHashSet(), ct);
            var cycle = await ReviewTotals.ReadAsync(
                db,
                batch.Games,
                async (appId, token) => (await steamApi.GetAppReviewsAsync(appId, "*", token))?.QuerySummary,
                today,
                ct);
            foreach (var appId in cycle.FailedAppIds)
                _failedAt[appId] = now;

            job.Set("listed", appIds.Count);
            job.Set("due", batch.Due);
            job.Set("read", cycle.Read);
            job.Set("failed", cycle.FailedAppIds.Count);
            job.SetItems(batch.Games.Count, cycle.FailedAppIds.Count, "games");
            if (cycle.FailedAppIds.Count > 0) job.MarkPartialSuccess();

            if (batch.Games.Count > 0)
                _logger.LogInformation(
                    "Review totals: read {Read} of {Selected} listed games ({Due} were due), {Failed} failed",
                    cycle.Read, batch.Games.Count, batch.Due, cycle.FailedAppIds.Count);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }
}
