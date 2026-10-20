using Cato.Domain.Entities;

namespace Cato.API.Services.JobRuns;

/// <summary>The editable part of a scheduled job.</summary>
public sealed record ScheduleSettings(
    string? Cron,
    bool Enabled,
    int TimeoutSeconds,
    decimal AlertFailedRatio,
    int AlertMinItems);

/// <summary>Checks and previews for schedule edits on the Jobs page. Cron times are UTC.</summary>
public static class ScheduledJobRules
{
    public const int MinTimeoutSeconds = 60;
    public const int MaxTimeoutSeconds = 24 * 3600;
    public const int MaxExpressions = 10;

    // Long enough to see a weekly expression twice and every daily job seven times.
    private static readonly TimeSpan GapHorizon = TimeSpan.FromDays(15);
    // Overlaps are reported for the next two days: enough for every daily job.
    private static readonly TimeSpan OverlapHorizon = TimeSpan.FromHours(48);

    public static IReadOnlyList<string> SplitCron(string? cron) =>
        string.IsNullOrWhiteSpace(cron)
            ? []
            : cron.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Normalized storage form: expressions trimmed and joined by " | "; null when empty.</summary>
    public static string? JoinCron(IEnumerable<string> expressions)
    {
        var list = expressions.Select(e => e.Trim()).Where(e => e.Length > 0).Distinct().ToList();
        return list.Count == 0 ? null : string.Join(" | ", list);
    }

    public static ScheduleSettings Of(ScheduledJob job) => new(
        job.Cron, job.Enabled, job.TimeoutSeconds, job.AlertFailedRatio, job.AlertMinItems);

    /// <summary>Problems that block a save. An empty list means the settings are valid.</summary>
    public static List<string> Validate(ScheduleSettings settings, DateTime nowUtc)
    {
        var errors = new List<string>();
        var expressions = SplitCron(settings.Cron);

        if (expressions.Count > MaxExpressions)
            errors.Add($"Use at most {MaxExpressions} cron expressions.");
        foreach (var expression in expressions)
        {
            if (!JobNextRunCalculator.TryParseCron(expression, out _))
                errors.Add($"'{expression}' is not a 5-field cron expression (minute hour day month weekday).");
        }

        if (settings.TimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
            errors.Add($"The time limit must be between {MinTimeoutSeconds / 60} minute and {MaxTimeoutSeconds / 3600} hours.");
        if (settings.AlertFailedRatio is < 0.01m or > 1m)
            errors.Add("The alert threshold must be between 1% and 100% failed items.");
        if (settings.AlertMinItems < 1)
            errors.Add("The minimum failed items for an alert must be 1 or more.");

        if (errors.Count == 0 && ShortestGap(expressions, nowUtc) is { } gap
            && gap <= TimeSpan.FromSeconds(settings.TimeoutSeconds))
        {
            errors.Add($"The job can start every {Minutes(gap)}, but its time limit is " +
                       $"{Minutes(TimeSpan.FromSeconds(settings.TimeoutSeconds))}. The interval must be longer than the time limit.");
        }

        return errors;
    }

    /// <summary>The next start times across all expressions, earliest first.</summary>
    public static List<DateTime> NextRuns(string? cron, DateTime nowUtc, int count = 5) =>
        Occurrences(SplitCron(cron), nowUtc, nowUtc + GapHorizon).Take(count).ToList();

    /// <summary>
    /// Runs of other jobs in the same agent group that would still be running when this job
    /// starts, or start while it runs. Not an error: the agent runs one job at a time, so the
    /// later one waits. One warning per other job, at its first overlap.
    /// </summary>
    public static List<string> OverlapWarnings(
        ScheduledJob job, ScheduleSettings settings, IEnumerable<ScheduledJob> others, DateTime nowUtc)
    {
        var warnings = new List<string>();
        if (!settings.Enabled) return warnings;

        var mine = Windows(SplitCron(settings.Cron), settings.TimeoutSeconds, nowUtc).ToList();
        if (mine.Count == 0) return warnings;

        foreach (var other in others.Where(o => o.Name != job.Name && o.AgentGroup == job.AgentGroup && o.Enabled))
        {
            var theirs = Windows(SplitCron(other.Cron), other.TimeoutSeconds, nowUtc);
            var clash = mine.SelectMany(a => theirs.Where(b => a.Start < b.End && b.Start < a.End)
                    .Select(b => a.Start > b.Start ? a.Start : b.Start))
                .Order()
                .FirstOrDefault();
            if (clash != default)
            {
                warnings.Add($"Can overlap \"{other.Label ?? other.Name}\" at {clash:HH:mm} UTC. " +
                             $"Agent {job.AgentGroup} runs one job at a time, so the later one waits.");
            }
        }

        return warnings;
    }

    private static IEnumerable<(DateTime Start, DateTime End)> Windows(
        IReadOnlyList<string> expressions, int timeoutSeconds, DateTime nowUtc)
    {
        // Start early enough to catch a run that began before now and is still inside its limit.
        var limit = TimeSpan.FromSeconds(timeoutSeconds);
        return Occurrences(expressions, nowUtc - limit, nowUtc + OverlapHorizon)
            .Select(start => (start, start + limit))
            .ToList();
    }

    private static TimeSpan? ShortestGap(IReadOnlyList<string> expressions, DateTime nowUtc)
    {
        var runs = Occurrences(expressions, nowUtc, nowUtc + GapHorizon).ToList();
        TimeSpan? shortest = null;
        for (var i = 1; i < runs.Count; i++)
        {
            var gap = runs[i] - runs[i - 1];
            if (shortest is null || gap < shortest) shortest = gap;
        }
        return shortest;
    }

    private static IEnumerable<DateTime> Occurrences(IReadOnlyList<string> expressions, DateTime fromUtc, DateTime toUtc)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        var all = new SortedSet<DateTime>();
        foreach (var expression in expressions)
        {
            if (!JobNextRunCalculator.TryParseCron(expression, out var cron)) continue;
            foreach (var occurrence in cron.GetOccurrences(from, to, TimeZoneInfo.Utc, fromInclusive: false))
                all.Add(occurrence);
        }
        return all;
    }

    private static string Minutes(TimeSpan span) =>
        span.TotalMinutes >= 120 && span.TotalMinutes % 60 == 0
            ? $"{span.TotalHours:0} hours"
            : $"{span.TotalMinutes:0} minutes";
}
