using Microsoft.Extensions.Options;

namespace Cato.API.Services.Dispatch;

/// <summary>
/// Turns due cron slots of dispatcher-managed jobs into dispatches, every
/// <see cref="JobDispatcherSettings.IntervalSeconds"/>, and expires the ones nobody runs.
/// Worker role only: there must be exactly one dispatcher.
/// </summary>
public sealed class JobDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly JobDispatcherSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<JobDispatcherService> _logger;

    public JobDispatcherService(
        IServiceScopeFactory scopes,
        IOptions<JobDispatcherSettings> settings,
        TimeProvider time,
        ILogger<JobDispatcherService> logger)
    {
        _scopes = scopes;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Job dispatcher disabled (JobDispatcher:Enabled is false)");
            return;
        }

        _logger.LogInformation("Job dispatcher started; checking every {Seconds}s", _settings.IntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _settings.IntervalSeconds)), _time);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var dispatch = scope.ServiceProvider.GetRequiredService<JobDispatchService>();
                var now = _time.GetUtcNow().UtcDateTime;
                var created = await dispatch.DispatchDueAsync(now, stoppingToken);
                var closed = await dispatch.HousekeepAsync(now, stoppingToken);
                if (created + closed > 0)
                    _logger.LogInformation("Job dispatcher: {Created} dispatched, {Closed} expired or lost", created, closed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One bad cycle must not stop the dispatcher; the next tick tries again.
                _logger.LogError(ex, "Job dispatcher cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
