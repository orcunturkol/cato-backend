using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cato.Infrastructure.Jobs;

/// <summary>A watcher's schedule from its settings: the first value of its scheduled_job row.</summary>
public sealed record WatcherDefaults(string Label, TimeSpan Interval, bool Enabled);

public sealed record WatcherScheduleState(TimeSpan Interval, bool Enabled);

/// <summary>
/// The backend watchers' schedule, from scheduled_job (ManagedBy "worker"), so the Jobs
/// page can change an interval or pause a watcher without a deploy. Each watcher calls
/// <see cref="WaitForNextRunAsync"/> before every cycle.
/// </summary>
public static class WatcherSchedule
{
    /// <summary>How often a waiting watcher reads its row again.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Waits until the watcher is enabled and its interval has passed since the end of the
    /// previous cycle (none yet: at once). The row is read again every minute, so a shorter
    /// interval or a resume takes effect within a minute. When the database cannot be read,
    /// the settings decide, so the watchers keep running.
    /// </summary>
    public static async Task WaitForNextRunAsync(
        IServiceScopeFactory scopes,
        string jobName,
        WatcherDefaults defaults,
        DateTime? previousEndUtc,
        ILogger logger,
        CancellationToken ct)
    {
        var pausedLogged = false;
        while (true)
        {
            WatcherScheduleState state;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
                state = await ReadAsync(db, jobName, defaults, DateTime.UtcNow, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("{Job}: cannot read its schedule ({Error}); using its settings", jobName, ex.Message);
                state = new WatcherScheduleState(defaults.Interval, defaults.Enabled);
            }

            if (!state.Enabled && !pausedLogged)
            {
                logger.LogInformation("{Job}: paused on the Jobs page; checking again every minute", jobName);
                pausedLogged = true;
            }
            if (TimeUntilDue(state, previousEndUtc, DateTime.UtcNow) is not { } wait)
                return;
            await Task.Delay(wait < PollInterval ? wait : PollInterval, ct);
        }
    }

    /// <summary>Null when the watcher should run now; otherwise how long it still waits.</summary>
    public static TimeSpan? TimeUntilDue(WatcherScheduleState state, DateTime? previousEndUtc, DateTime nowUtc)
    {
        if (!state.Enabled)
            return PollInterval;
        if (previousEndUtc is not { } end)
            return null;
        var due = end + state.Interval;
        return nowUtc >= due ? null : due - nowUtc;
    }

    /// <summary>The watcher's row; the first read writes it from the settings.</summary>
    public static async Task<WatcherScheduleState> ReadAsync(
        CatoDbContext db, string jobName, WatcherDefaults defaults, DateTime nowUtc, CancellationToken ct)
    {
        var row = await db.ScheduledJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Name == jobName, ct);
        if (row is null)
        {
            row = new ScheduledJob
            {
                Name = jobName,
                Label = defaults.Label,
                AgentGroup = JobAgentGroups.Worker,
                Module = string.Empty,
                Args = string.Empty,
                Cron = null,
                IntervalMinutes = Math.Max(1, (int)defaults.Interval.TotalMinutes),
                Enabled = defaults.Enabled,
                TimeoutSeconds = 0,
                ManagedBy = ScheduledJobManagedBy.Worker,
                UpdatedAt = nowUtc,
                UpdatedBy = "settings",
            };
            db.ScheduledJobs.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another process wrote it first; its row wins.
                db.ChangeTracker.Clear();
                row = await db.ScheduledJobs.AsNoTracking().FirstAsync(j => j.Name == jobName, ct);
            }
        }

        var minutes = row.IntervalMinutes is > 0 ? row.IntervalMinutes.Value : (int)defaults.Interval.TotalMinutes;
        return new WatcherScheduleState(TimeSpan.FromMinutes(Math.Max(1, minutes)), row.Enabled);
    }
}
