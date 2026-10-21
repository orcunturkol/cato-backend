using System.Globalization;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.JobRuns;

/// <summary>
/// When the process that runs the backend watchers last started. A watcher run that is still
/// "Running" but started before that is gone. In role all or worker this process is that
/// process; in role api the worker's start comes from worker_state, so an API-only restart
/// no longer marks the worker's runs lost.
/// </summary>
public sealed class WorkerStartTime
{
    private readonly CatoDbContext _db;
    private readonly ApiProcessInfo _process;
    private readonly bool _thisProcessRunsWatchers;

    public WorkerStartTime(CatoDbContext db, ApiProcessInfo process, bool thisProcessRunsWatchers)
    {
        _db = db;
        _process = process;
        _thisProcessRunsWatchers = thisProcessRunsWatchers;
    }

    /// <summary>DateTime.MinValue when no worker has recorded its start: no run is lost by a restart then.</summary>
    public async Task<DateTime> GetAsync(CancellationToken ct)
    {
        if (_thisProcessRunsWatchers)
            return _process.StartedAtUtc;
        var value = await _db.WorkerStates.AsNoTracking()
            .Where(s => s.Key == WorkerStateKeys.WorkerStartedAt)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var started)
            ? started
            : DateTime.MinValue;
    }
}

/// <summary>Writes this worker's start time to worker_state, for API processes to read.</summary>
public sealed class WorkerStartRecorder : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ApiProcessInfo _process;
    private readonly ILogger<WorkerStartRecorder> _logger;

    public WorkerStartRecorder(IServiceScopeFactory scopes, ApiProcessInfo process, ILogger<WorkerStartRecorder> logger)
    {
        _scopes = scopes;
        _process = process;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await WorkerStateStore.SetAsync(_scopes, WorkerStateKeys.WorkerStartedAt,
                _process.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without it an API process cannot tell lost runs by restart; the worker still runs.
            _logger.LogWarning("Could not record the worker start time: {Error}", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
