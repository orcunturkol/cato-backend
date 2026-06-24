using Cato.Domain.Entities;

namespace Cato.Infrastructure.Jobs;

/// <summary>
/// Records one execution of a tracked job to the <c>job_run</c> table. Resolve it
/// inside a watcher's cycle, open a scope with <see cref="StartAsync"/> (which writes
/// a <see cref="JobRunStatus.Running"/> row immediately), record counters on the
/// returned scope, then dispose it — disposal stamps the end time, resolves the final
/// status, persists the metrics bag, and emits a structured "JobRun finished" log event.
/// </summary>
public interface IJobRunTracker
{
    Task<IJobRunScope> StartAsync(
        string jobName,
        string producer = JobRunProducer.CatoBackend,
        CancellationToken ct = default);
}

/// <summary>
/// A live job run. Record per-job counters via <see cref="Set"/>/<see cref="Increment"/>.
/// Call <see cref="Fail"/> on error (or just let the exception propagate and call it from
/// the watcher's catch) and <see cref="MarkPartialSuccess"/> when the run produced output
/// but some items failed. The run is finalized on dispose.
/// </summary>
public interface IJobRunScope : IAsyncDisposable
{
    /// <summary>Set (overwrite) a counter.</summary>
    void Set(string key, long value);

    /// <summary>Add to a counter, creating it at <paramref name="by"/> if absent.</summary>
    void Increment(string key, long by = 1);

    /// <summary>Mark the run as failed with a message (final status becomes Failed).</summary>
    void Fail(string message);

    /// <summary>Mark the run as partially successful (final status becomes PartialSuccess unless failed).</summary>
    void MarkPartialSuccess();
}
