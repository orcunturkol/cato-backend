using System.Text.Json;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cato.Infrastructure.Jobs;

/// <summary>
/// Default <see cref="IJobRunTracker"/>. Each run gets its own DI scope + DbContext so
/// the job_run row write is fully isolated from the caller's transaction state (a
/// watcher's partial/failed work never leaks into — or blocks — the run record).
/// </summary>
public sealed class JobRunTracker : IJobRunTracker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobRunTracker> _logger;

    public JobRunTracker(IServiceScopeFactory scopeFactory, ILogger<JobRunTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IJobRunScope> StartAsync(
        string jobName,
        string producer = JobRunProducer.CatoBackend,
        CancellationToken ct = default)
    {
        var scope = _scopeFactory.CreateScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
            var run = new JobRun
            {
                Id = Guid.NewGuid(),
                JobName = jobName,
                Producer = producer,
                StartTime = DateTime.UtcNow,
                Status = JobRunStatus.Running,
            };
            db.JobRuns.Add(run);
            await db.SaveChangesAsync(ct);
            return new JobRunScope(scope, db, run, _logger);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private sealed class JobRunScope : IJobRunScope
    {
        private readonly IServiceScope _scope;
        private readonly CatoDbContext _db;
        private readonly JobRun _run;
        private readonly ILogger _logger;
        private readonly Dictionary<string, long> _metrics = new();

        private bool _failed;
        private string? _failMessage;
        private bool _partial;
        private bool _disposed;

        public JobRunScope(IServiceScope scope, CatoDbContext db, JobRun run, ILogger logger)
        {
            _scope = scope;
            _db = db;
            _run = run;
            _logger = logger;
        }

        public void Set(string key, long value) => _metrics[key] = value;

        public void Increment(string key, long by = 1) =>
            _metrics[key] = _metrics.TryGetValue(key, out var v) ? v + by : by;

        public void Fail(string message)
        {
            _failed = true;
            _failMessage = message;
        }

        public void MarkPartialSuccess() => _partial = true;

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                var status = _failed
                    ? JobRunStatus.Failed
                    : _partial ? JobRunStatus.PartialSuccess : JobRunStatus.Succeeded;

                var end = DateTime.UtcNow;
                _run.EndTime = end;
                _run.DurationMs = (long)(end - _run.StartTime).TotalMilliseconds;
                _run.Status = status;
                _run.ErrorMessage = _failMessage;
                _run.MetricsJson = _metrics.Count > 0 ? JsonSerializer.Serialize(_metrics) : null;

                // Not cancelled by the caller's token on purpose — we always want the run recorded.
                await _db.SaveChangesAsync();

                _logger.Log(
                    status == JobRunStatus.Failed ? LogLevel.Error : LogLevel.Information,
                    "JobRun {JobName} finished with {Status} in {DurationMs}ms (producer={Producer}) {@Metrics}",
                    _run.JobName, status, _run.DurationMs, _run.Producer, _metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist JobRun {JobName}", _run.JobName);
            }
            finally
            {
                _scope.Dispose();
            }
        }
    }
}
