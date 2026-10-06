using Cato.Domain.Entities;

namespace Cato.API.Services.JobRuns;

/// <summary>
/// Turns a stored status into the status to show. Only "Running" can change:
/// it becomes "Lost" when the run can no longer report a finish.
/// </summary>
public static class JobRunStatusEvaluator
{
    public const string ReasonNewerRun = "a later run of this job started";
    public const string ReasonApiRestarted = "the API restarted during this run";

    public static (string Status, string? LostReason) Evaluate(
        JobRun run,
        bool newerRunExists,
        TimeSpan staleAfter,
        DateTime nowUtc,
        DateTime apiStartedAtUtc)
    {
        if (run.Status != JobRunStatus.Running)
            return (run.Status, null);

        // Watchers run one cycle at a time and collector jobs hold a lock.
        if (newerRunExists)
            return (JobRunStatus.Lost, ReasonNewerRun);

        if (run.Producer == JobRunProducer.CatoBackend && run.StartTime < apiStartedAtUtc)
            return (JobRunStatus.Lost, ReasonApiRestarted);

        if (nowUtc - run.StartTime > staleAfter)
            return (JobRunStatus.Lost, $"no finish reported within {FormatSpan(staleAfter)}");

        return (JobRunStatus.Running, null);
    }

    /// <summary>
    /// Evaluates one job's runs, newest first. A row's newer runs are the ones before it,
    /// so a window of the latest N rows is enough.
    /// </summary>
    public static List<(JobRun Run, string Status, string? LostReason)> EvaluateNewestFirst(
        IReadOnlyList<JobRun> newestFirst,
        TimeSpan staleAfter,
        DateTime nowUtc,
        DateTime apiStartedAtUtc)
    {
        var result = new List<(JobRun, string, string?)>(newestFirst.Count);
        var newerSeen = false;
        foreach (var run in newestFirst)
        {
            var (status, reason) = Evaluate(run, newerSeen, staleAfter, nowUtc, apiStartedAtUtc);
            result.Add((run, status, reason));
            if (run.Status != JobRunStatus.Skipped)
                newerSeen = true;
        }
        return result;
    }

    public static string FormatSpan(TimeSpan span) =>
        span.TotalHours >= 1 && span.Minutes == 0
            ? $"{(int)span.TotalHours}h"
            : span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : $"{(int)span.TotalMinutes}m";
}
