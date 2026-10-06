namespace Cato.API.Services.JobRuns;

public static class JobScheduleKind
{
    public const string Cron = "cron";
    public const string Interval = "interval";
    public const string None = "none";
}

/// <summary>What the API knows about one job: how it is scheduled and when a run counts as lost.</summary>
public sealed record JobDefinition(
    string JobName,
    string Producer,
    string? Label,
    string ScheduleKind,
    IReadOnlyList<string> CronExpressions,
    int? IntervalMinutes,
    bool Enabled,
    string? DisabledReason,
    int? TimeoutSeconds,
    TimeSpan StaleAfter,
    TimeSpan InitialDelay)
{
    /// <summary>A job seen in job_run that no schedule knows about (manual runs, removed jobs).</summary>
    public static JobDefinition Unscheduled(string producer, string jobName) => new(
        jobName, producer, null, JobScheduleKind.None, [], null,
        Enabled: true, DisabledReason: null, TimeoutSeconds: null,
        JobRunRules.DefaultStaleAfter, TimeSpan.Zero);
}

/// <summary>Thresholds for the "lost" rule.</summary>
public static class JobRunRules
{
    /// <summary>Extra time after a hard timeout before a silent run counts as lost.</summary>
    public static readonly TimeSpan TimeoutGrace = TimeSpan.FromMinutes(10);

    /// <summary>Limit for a job with no timeout and no interval.</summary>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(6);

    public static TimeSpan ForTimeout(int? timeoutSeconds) =>
        timeoutSeconds is > 0
            ? TimeSpan.FromSeconds(timeoutSeconds.Value) + TimeoutGrace
            : DefaultStaleAfter;

    /// <summary>Interval watchers: twice the interval, at least one hour.</summary>
    public static TimeSpan ForInterval(TimeSpan interval)
    {
        var doubled = interval + interval;
        return doubled < TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : doubled;
    }
}

/// <summary>When this API process started; runs it owned before that are gone.</summary>
public sealed record ApiProcessInfo(DateTime StartedAtUtc);
