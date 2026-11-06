using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Redis;

/// <summary>
/// Keeps the follower-watch sets in step with the "new pages" games: new ones in the daily
/// set, older ones in the weekly set, tracked and filtered games out. The collector's
/// follower_watch job does the reading; this only writes members. See <see cref="FollowerWatchQueue"/>.
/// </summary>
public sealed class FollowerWatchSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFollowerWatchQueueService _queue;
    private readonly ILogger<FollowerWatchSweepService> _logger;
    private readonly FollowerWatchSettings _settings;

    public FollowerWatchSweepService(
        IServiceScopeFactory scopeFactory,
        IFollowerWatchQueueService queue,
        ILogger<FollowerWatchSweepService> logger,
        IOptions<FollowerWatchSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
        _settings = settings.Value;
    }

    // The first value of this watcher's scheduled_job row; the Jobs page owns it after that.
    private WatcherDefaults Defaults => new("Follower watch sweep", TimeSpan.FromMinutes(_settings.IntervalMinutes), _settings.Enabled);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FollowerWatchSweep: starting — every {Minutes} min", _settings.IntervalMinutes);

        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        DateTime? previousEnd = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            await WatcherSchedule.WaitForNextRunAsync(
                _scopeFactory, BackendJobNames.FollowerWatchSweep, Defaults, previousEnd, _logger, stoppingToken);

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
                _logger.LogError(ex, "FollowerWatchSweep: cycle failed");
            }

            previousEnd = DateTime.UtcNow;
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var tracker = scope.ServiceProvider.GetRequiredService<IJobRunTracker>();

        await using var job = await tracker.StartAsync(BackendJobNames.FollowerWatchSweep, ct: ct);
        try
        {
            var candidates = await FollowerWatchQueue.LoadCandidatesAsync(db, _settings.DailyDays, DateTime.UtcNow, ct);
            var daily = await _queue.GetAllAsync(FollowerWatchQueue.DailyKey, ct);
            var weekly = await _queue.GetAllAsync(FollowerWatchQueue.WeeklyKey, ct);
            var quarantined = await _queue.GetQuarantinedAsync(FollowerWatchQueue.DailyKey, ct);
            quarantined.UnionWith(await _queue.GetQuarantinedAsync(FollowerWatchQueue.WeeklyKey, ct));

            var plan = FollowerWatchQueue.Plan(candidates, daily, weekly, quarantined);
            // Add before remove: a game moving to the weekly set is never in neither.
            await _queue.AddAsync(FollowerWatchQueue.DailyKey, plan.AddDaily, ct);
            await _queue.AddAsync(FollowerWatchQueue.WeeklyKey, plan.AddWeekly, ct);
            await _queue.RemoveAsync(FollowerWatchQueue.DailyKey, plan.RemoveDaily, ct);
            await _queue.RemoveAsync(FollowerWatchQueue.WeeklyKey, plan.RemoveWeekly, ct);

            job.Set("daily", candidates.Daily.Count);
            job.Set("weekly", candidates.Weekly.Count);
            job.Set("quarantined", quarantined.Count);
            job.Set("added_daily", plan.AddDaily.Count);
            job.Set("added_weekly", plan.AddWeekly.Count);
            job.Set("removed_daily", plan.RemoveDaily.Count);
            job.Set("removed_weekly", plan.RemoveWeekly.Count);

            _logger.LogInformation(
                "FollowerWatchSweep: {Daily} daily and {Weekly} weekly games; added {AddDaily}/{AddWeekly}, removed {RemoveDaily}/{RemoveWeekly} ({Quarantined} quarantined)",
                candidates.Daily.Count, candidates.Weekly.Count, plan.AddDaily.Count, plan.AddWeekly.Count,
                plan.RemoveDaily.Count, plan.RemoveWeekly.Count, quarantined.Count);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }
}
