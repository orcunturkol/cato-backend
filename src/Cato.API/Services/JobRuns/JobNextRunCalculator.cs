using Cato.API.Models.JobRuns;
using Cato.Domain.Entities;
using Cronos;

namespace Cato.API.Services.JobRuns;

/// <summary>Next expected start of a job. All times are UTC.</summary>
public static class JobNextRunCalculator
{
    public const string NoteAfterCurrentRun = "after the current run";
    public const string NoteFirstRunSinceStart = "first run since API start";
    public const string NoteOverdue = "overdue";

    // Allowed lateness before an interval job counts as overdue.
    private static readonly TimeSpan OverdueSlack = TimeSpan.FromMinutes(2);

    public static (DateTime? NextRunAt, string? Note) Calculate(
        JobDefinition job,
        JobRunDto? lastRun,
        DateTime nowUtc,
        DateTime apiStartedAtUtc)
    {
        if (!job.Enabled)
            return (null, job.DisabledReason ?? "disabled");

        return job.ScheduleKind switch
        {
            JobScheduleKind.Cron => (NextCronOccurrence(job.CronExpressions, nowUtc), null),
            JobScheduleKind.Interval => NextIntervalRun(job, lastRun, nowUtc, apiStartedAtUtc),
            _ => (null, null),
        };
    }

    public static DateTime? NextCronOccurrence(IEnumerable<string> expressions, DateTime nowUtc)
    {
        var from = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        DateTime? best = null;
        foreach (var expression in expressions)
        {
            if (!TryParseCron(expression, out var cron)) continue;
            var next = cron.GetNextOccurrence(from, TimeZoneInfo.Utc);
            if (next is not null && (best is null || next < best)) best = next;
        }
        return best;
    }

    public static bool TryParseCron(string? expression, out CronExpression cron)
    {
        cron = null!;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        try
        {
            cron = CronExpression.Parse(expression.Trim(), CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    // Watchers loop "run, then wait the interval", so the next start follows the last end.
    private static (DateTime?, string?) NextIntervalRun(
        JobDefinition job, JobRunDto? lastRun, DateTime nowUtc, DateTime apiStartedAtUtc)
    {
        if (job.IntervalMinutes is not > 0) return (null, null);
        var interval = TimeSpan.FromMinutes(job.IntervalMinutes.Value);

        if (lastRun?.EffectiveStatus == JobRunStatus.Running)
            return (null, NoteAfterCurrentRun);

        DateTime next;
        string? note = null;
        if (lastRun is null || lastRun.StartTime < apiStartedAtUtc)
        {
            next = apiStartedAtUtc + job.InitialDelay;
            note = NoteFirstRunSinceStart;
        }
        else
        {
            next = (lastRun.EndTime ?? lastRun.StartTime) + interval;
        }

        if (next + OverdueSlack < nowUtc)
            note = note is null ? NoteOverdue : $"{note}, {NoteOverdue}";

        return (DateTime.SpecifyKind(next, DateTimeKind.Utc), note);
    }
}
