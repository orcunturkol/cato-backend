using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Redis;

/// <summary>
/// Queues follower-history fetches for the games CATO follows automatically: every
/// Owned game (refreshed daily) and every game in a SteamDB list — most wished or
/// wishlist activity — in rank order. The collector's follower job does the fetching;
/// this only writes scores. See <see cref="FollowerHistoryQueue"/> for the bands.
/// </summary>
public sealed class FollowerHistorySweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFollowerHistoryQueueService _queue;
    private readonly ILogger<FollowerHistorySweepService> _logger;
    private readonly FollowerHistorySweepSettings _settings;

    public FollowerHistorySweepService(
        IServiceScopeFactory scopeFactory,
        IFollowerHistoryQueueService queue,
        ILogger<FollowerHistorySweepService> logger,
        IOptions<FollowerHistorySweepSettings> settings)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("FollowerHistorySweep: disabled by configuration");
            return;
        }

        _logger.LogInformation("FollowerHistorySweep: starting — every {Minutes} min", _settings.IntervalMinutes);

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
                _logger.LogError(ex, "FollowerHistorySweep: cycle failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_settings.IntervalMinutes), stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var tracker = scope.ServiceProvider.GetRequiredService<IJobRunTracker>();

        await using var job = await tracker.StartAsync(BackendJobNames.FollowerHistorySweep, ct: ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var candidates = await FollowerHistoryQueue.LoadCandidatesAsync(
                db, _settings, DateOnly.FromDateTime(now.UtcDateTime), ct);

            var appIds = candidates.AppIds.ToList();
            var scores = await _queue.GetScoresAsync(appIds, ct);
            var quarantined = await _queue.GetQuarantinedAsync(appIds, ct);
            var writes = FollowerHistoryQueue.Plan(candidates, scores, quarantined, now, _settings);
            await _queue.SetScoresAsync(writes, ct);

            var ownedQueued = writes.Count(w => w.Value == FollowerHistoryQueue.OwnedScore);
            job.Set("owned", candidates.Owned.Count);
            job.Set("listed", candidates.ListRanks.Count);
            job.Set("quarantined", quarantined.Count);
            job.Set("owned_queued", ownedQueued);
            job.Set("listed_queued", writes.Count - ownedQueued);

            _logger.LogInformation(
                "FollowerHistorySweep: queued {Owned} owned and {Listed} listed games ({Candidates} candidates, {Quarantined} quarantined)",
                ownedQueued, writes.Count - ownedQueued, appIds.Count, quarantined.Count);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }
}
