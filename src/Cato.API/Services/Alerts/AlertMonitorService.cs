using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Microsoft.Extensions.Options;

namespace Cato.API.Services.Alerts;

/// <summary>
/// Runs an alert cycle every <see cref="AlertSettings.IntervalSeconds"/> and sends the
/// CATO/WorkerHeartbeat metric; a CloudWatch alarm on that metric reports a dead worker.
/// Worker role only: two monitors would send every message twice.
/// </summary>
public sealed class AlertMonitorService : BackgroundService
{
    // After a restart the agents need a few polls to be seen again, and the dispatcher a
    // few ticks to catch up; without this wait every deploy would alert.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly AlertSettings _settings;
    private readonly IAmazonCloudWatch? _cloudWatch;
    private readonly TimeProvider _time;
    private readonly ILogger<AlertMonitorService> _logger;

    public AlertMonitorService(
        IServiceScopeFactory scopes,
        IOptions<AlertSettings> settings,
        TimeProvider time,
        ILogger<AlertMonitorService> logger,
        IAmazonCloudWatch? cloudWatch = null)
    {
        _scopes = scopes;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
        _cloudWatch = cloudWatch;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Alert monitor disabled (Alerts:Enabled is false)");
            return;
        }
        _logger.LogInformation("Alert monitor started; checking every {Seconds}s after {Delay}",
            _settings.IntervalSeconds, StartupDelay);
        try
        {
            await Task.Delay(StartupDelay, _time, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, _settings.IntervalSeconds)), _time);
        do
        {
            await SendHeartbeatAsync(stoppingToken);
            try
            {
                using var scope = _scopes.CreateScope();
                var alerts = scope.ServiceProvider.GetRequiredService<AlertService>();
                var result = await alerts.RunCycleAsync(_time.GetUtcNow().UtcDateTime, stoppingToken);
                if (result.Opened + result.Resolved + result.Sent > 0)
                    _logger.LogInformation("Alert cycle: {Opened} opened, {Resolved} resolved, {Sent} message(s) sent",
                        result.Opened, result.Resolved, result.Sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One bad cycle must not stop the monitor; the next tick tries again.
                _logger.LogError(ex, "Alert cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        if (!_settings.HeartbeatEnabled || _cloudWatch is null)
            return;
        try
        {
            // The instance role may write only to the "CATO" namespace.
            await _cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
            {
                Namespace = "CATO",
                MetricData =
                [
                    new MetricDatum { MetricName = "WorkerHeartbeat", Value = 1, Unit = StandardUnit.Count },
                ],
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Worker heartbeat metric failed: {Error}", ex.Message);
        }
    }
}
