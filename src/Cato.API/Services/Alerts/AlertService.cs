using System.Globalization;
using System.Text.Json;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cato.API.Services.Alerts;

/// <summary>One rule's verdict for one subject: firing opens or keeps an incident, not firing resolves it.</summary>
public sealed record AlertCheck(string JobName, string Rule, bool Firing, string? Message = null, Guid? RunId = null);

public sealed record AlertCycleResult(int Checks, int Opened, int Resolved, int Sent);

public sealed record AlertChannelResult(string Channel, bool Sent, string? Error);

/// <summary>
/// When each queue last made progress, across cycles (singleton). A passive declare shows
/// only the count, so a count that does not fall is "no progress".
/// </summary>
public sealed class QueueProgressTracker
{
    private readonly Dictionary<string, (uint Messages, DateTime ProgressAt)> _state = new();
    private readonly Lock _lock = new();

    /// <summary>How long the queue has had messages without a drop in their count.</summary>
    public TimeSpan Observe(QueueStats queue, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(queue.Queue, out var previous)
                || queue.Messages == 0 || queue.Messages < previous.Messages)
            {
                _state[queue.Queue] = (queue.Messages, nowUtc);
                return TimeSpan.Zero;
            }
            _state[queue.Queue] = (queue.Messages, previous.ProgressAt);
            return nowUtc - previous.ProgressAt;
        }
    }
}

/// <summary>
/// One alert cycle: evaluate every rule on the current state, open and resolve incidents,
/// then send what each channel has not sent yet. A failed channel is retried next cycle.
/// </summary>
public sealed class AlertService
{
    private static readonly TimeSpan AgentWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromDays(7);
    private const int RunsPerJob = 20;

    private readonly CatoDbContext _db;
    private readonly IJobCatalog _catalog;
    private readonly ApiProcessInfo _process;
    private readonly IReadOnlyList<IAlertNotifier> _notifiers;
    private readonly IQueueStatsReader _queues;
    private readonly QueueProgressTracker _progress;
    private readonly AlertSettings _settings;
    private readonly string _deadLetterQueue;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        CatoDbContext db,
        IJobCatalog catalog,
        ApiProcessInfo process,
        IEnumerable<IAlertNotifier> notifiers,
        IQueueStatsReader queues,
        QueueProgressTracker progress,
        IOptions<AlertSettings> settings,
        IOptions<Cato.Infrastructure.Messaging.RabbitMqSettings> rabbit,
        ILogger<AlertService> logger)
    {
        _db = db;
        _catalog = catalog;
        _process = process;
        _notifiers = notifiers.ToList();
        _queues = queues;
        _progress = progress;
        _settings = settings.Value;
        _deadLetterQueue = rabbit.Value.DeadLetterQueue;
        _logger = logger;
    }

    public async Task<AlertCycleResult> RunCycleAsync(DateTime nowUtc, CancellationToken ct)
    {
        var checks = new List<AlertCheck>();
        checks.AddRange(await CheckRunsAsync(nowUtc, ct));
        checks.AddRange(await CheckDispatchesAsync(nowUtc, ct));
        checks.AddRange(await CheckAgentsAsync(nowUtc, ct));
        checks.AddRange(await CheckQueuesAsync(nowUtc, ct));
        var (opened, resolved) = await ApplyAsync(checks, nowUtc, ct);
        var sent = await DeliverAsync(nowUtc, ct);
        return new AlertCycleResult(checks.Count, opened, resolved, sent);
    }

    // ── rules ──

    /// <summary>run_failed and items_failed, from the newest finished run of each enabled job.</summary>
    public async Task<List<AlertCheck>> CheckRunsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var catalog = await _catalog.LoadAsync(ct);
        var thresholds = await _db.ScheduledJobs.AsNoTracking()
            .ToDictionaryAsync(j => j.Name, j => (j.AlertFailedRatio, j.AlertMinItems), ct);
        var checks = new List<AlertCheck>();

        foreach (var job in catalog.Jobs.Where(j => j.Enabled))
        {
            var collector = job.Producer == JobRunProducer.ExternalCollector;
            var runs = await _db.JobRuns.AsNoTracking()
                .Where(r => r.JobName == job.JobName && r.Producer == job.Producer)
                .OrderByDescending(r => r.StartTime)
                .Take(RunsPerJob)
                .ToListAsync(ct);
            var evaluated = JobRunStatusEvaluator.EvaluateNewestFirst(
                runs, job.StaleAfter, nowUtc, _process.StartedAtUtc);

            // Skipped, Interrupted and live runs decide nothing. A watcher run shows Lost after
            // every API restart until the Lost rule learns about worker restarts, so for
            // watchers only a stored Failed counts.
            var decisive = evaluated.FirstOrDefault(e =>
                e.Status is JobRunStatus.Succeeded or JobRunStatus.PartialSuccess or JobRunStatus.Failed
                || (collector && e.Status == JobRunStatus.Lost));
            if (decisive.Run is not { } run)
                continue;

            var started = $"the run started {Stamp(run.StartTime)} UTC";
            if (decisive.Status is JobRunStatus.Failed or JobRunStatus.Lost)
            {
                var reason = run.ErrorMessage ?? decisive.LostReason ?? "no reason given";
                checks.Add(new AlertCheck(job.JobName, AlertRules.RunFailed, true,
                    $"{decisive.Status}: {reason} ({started})", run.Id));
                continue;
            }

            checks.Add(new AlertCheck(job.JobName, AlertRules.RunFailed, false));

            if (ReadItems(run.MetricsJson) is not { Total: > 0 } items)
                continue;
            var (ratio, minItems) = thresholds.TryGetValue(job.JobName, out var t) ? t : (0.10m, 1);
            var share = (decimal)items.Failed / items.Total;
            var firing = items.Failed >= minItems && share >= ratio;
            checks.Add(new AlertCheck(job.JobName, AlertRules.ItemsFailed, firing,
                firing
                    ? $"{items.Failed} of {items.Total} {items.Unit} failed ({share:P1}; the alert level is {ratio:P0}) in {started}"
                    : null,
                run.Id));
        }
        return checks;
    }

    /// <summary>missed_run: the newest dispatch of each dispatcher job waits too long or expired.</summary>
    public async Task<List<AlertCheck>> CheckDispatchesAsync(DateTime nowUtc, CancellationToken ct)
    {
        var jobs = await _db.ScheduledJobs.AsNoTracking()
            .Where(j => j.Enabled && j.ManagedBy == ScheduledJobManagedBy.Dispatcher)
            .ToListAsync(ct);
        var busyGroups = (await _db.JobDispatches.AsNoTracking()
            .Where(d => d.Status == JobDispatchStatus.Claimed)
            .Select(d => d.AgentGroup)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();
        var grace = TimeSpan.FromMinutes(_settings.MissedGraceMinutes);
        var checks = new List<AlertCheck>();

        foreach (var job in jobs)
        {
            var newest = await _db.JobDispatches.AsNoTracking()
                .Where(d => d.JobName == job.Name)
                .OrderByDescending(d => d.ScheduledAt)
                .FirstOrDefaultAsync(ct);
            if (newest is null)
                continue;

            var slot = $"the {newest.Kind} run of {Stamp(newest.ScheduledAt)} UTC";
            switch (newest.Status)
            {
                // A pending run behind another job of its group waits by design.
                case JobDispatchStatus.Pending when nowUtc - newest.ScheduledAt > grace && !busyGroups.Contains(newest.AgentGroup):
                    var minutes = (int)(nowUtc - newest.ScheduledAt).TotalMinutes;
                    checks.Add(new AlertCheck(job.Name, AlertRules.MissedRun, true,
                        $"{slot} has not started after {minutes} min, and agent group {newest.AgentGroup} runs nothing"));
                    break;
                case JobDispatchStatus.Expired:
                    checks.Add(new AlertCheck(job.Name, AlertRules.MissedRun, true,
                        $"{slot} expired: no agent of group {newest.AgentGroup} claimed it"));
                    break;
                case JobDispatchStatus.Claimed or JobDispatchStatus.Finished:
                    checks.Add(new AlertCheck(job.Name, AlertRules.MissedRun, false));
                    break;
            }
        }
        return checks;
    }

    /// <summary>agent_offline: an agent seen this week has stopped calling the API.</summary>
    public async Task<List<AlertCheck>> CheckAgentsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var since = nowUtc - AgentWindow;
        var agents = await _db.JobAgents.AsNoTracking().Where(a => a.LastSeenAt >= since).ToListAsync(ct);
        var limit = TimeSpan.FromMinutes(_settings.AgentOfflineMinutes);
        return agents.Select(a =>
        {
            var silent = nowUtc - AsUtc(a.LastSeenAt);
            return silent > limit
                ? new AlertCheck($"agent:{a.AgentId}", AlertRules.AgentOffline, true,
                    $"{a.AgentId} last called the API {(int)silent.TotalMinutes} min ago ({Stamp(a.LastSeenAt)} UTC)")
                : new AlertCheck($"agent:{a.AgentId}", AlertRules.AgentOffline, false);
        }).ToList();
    }

    /// <summary>dlq_not_empty and queue_stuck. A broker that cannot be read decides nothing.</summary>
    public async Task<List<AlertCheck>> CheckQueuesAsync(DateTime nowUtc, CancellationToken ct)
    {
        IReadOnlyList<QueueStats> stats;
        try
        {
            stats = await _queues.ReadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Alert cycle could not read the queues");
            return [];
        }

        var stuckAfter = TimeSpan.FromMinutes(_settings.QueueStuckMinutes);
        var checks = new List<AlertCheck>();
        foreach (var queue in stats)
        {
            var subject = $"queue:{queue.Queue}";
            if (queue.Queue == _deadLetterQueue)
            {
                checks.Add(new AlertCheck(subject, AlertRules.DlqNotEmpty, queue.Messages > 0,
                    queue.Messages > 0
                        ? $"{queue.Messages} rejected batch(es) wait in {queue.Queue}. Find the cause in the API log, fix it, then replay them."
                        : null));
                continue;
            }

            var stuckFor = _progress.Observe(queue, nowUtc);
            var firing = queue.Messages > 0 && stuckFor >= stuckAfter;
            checks.Add(new AlertCheck(subject, AlertRules.QueueStuck, firing,
                firing
                    ? $"{queue.Messages} message(s) and {queue.Consumers} consumer(s); the count has not dropped for {(int)stuckFor.TotalMinutes} min"
                    : null));
        }
        return checks;
    }

    // ── incidents ──

    public async Task<(int Opened, int Resolved)> ApplyAsync(IReadOnlyList<AlertCheck> checks, DateTime nowUtc, CancellationToken ct)
    {
        var open = await _db.AlertIncidents.Where(i => i.ResolvedAt == null).ToListAsync(ct);
        var byKey = open.ToDictionary(i => (i.JobName, i.Rule));
        int opened = 0, resolved = 0;

        foreach (var check in checks)
        {
            byKey.TryGetValue((check.JobName, check.Rule), out var incident);
            if (check.Firing)
            {
                if (incident is null)
                {
                    incident = new AlertIncident
                    {
                        Id = Guid.NewGuid(),
                        JobName = check.JobName,
                        Rule = check.Rule,
                        OpenKey = $"{check.JobName}|{check.Rule}",
                        OpenedAt = nowUtc,
                    };
                    _db.AlertIncidents.Add(incident);
                    byKey[(check.JobName, check.Rule)] = incident;
                    opened++;
                    _logger.LogWarning("Alert opened: {Job} {Rule}: {Message}", check.JobName, check.Rule, check.Message);
                }
                incident.LastSeenAt = nowUtc;
                incident.Message = check.Message ?? incident.Message;
                incident.RunId = check.RunId ?? incident.RunId;
            }
            else if (incident is not null && incident.ResolvedAt is null)
            {
                incident.ResolvedAt = nowUtc;
                incident.LastSeenAt = nowUtc;
                incident.OpenKey = null;
                resolved++;
                _logger.LogInformation("Alert resolved: {Job} {Rule}", check.JobName, check.Rule);
            }
        }
        await _db.SaveChangesAsync(ct);
        return (opened, resolved);
    }

    /// <summary>
    /// Sends each "opened" message to the channels that have not sent it, and each
    /// "resolved" message to the channels that sent the "opened" one.
    /// </summary>
    public async Task<int> DeliverAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (_notifiers.Count == 0)
            return 0;
        var since = nowUtc - DeliveryWindow;
        var incidents = await _db.AlertIncidents
            .Where(i => i.ResolvedAt == null || i.ResolvedAt >= since)
            .OrderBy(i => i.OpenedAt)
            .ToListAsync(ct);
        var sent = 0;

        foreach (var incident in incidents)
        {
            var openSent = Channels(incident.OpenSentTo);
            var resolveSent = Channels(incident.ResolveSentTo);
            List<(IAlertNotifier Notifier, bool Resolve)> due = incident.ResolvedAt is null
                ? _notifiers.Where(n => !openSent.Contains(n.Name)).Select(n => (n, false)).ToList()
                : _notifiers.Where(n => openSent.Contains(n.Name) && !resolveSent.Contains(n.Name)).Select(n => (n, true)).ToList();
            if (due.Count == 0)
                continue;

            foreach (var (notifier, resolve) in due)
            {
                try
                {
                    await notifier.SendAsync(resolve ? Resolved(incident, nowUtc) : Opened(incident), ct);
                    if (resolve) resolveSent.Add(notifier.Name); else openSent.Add(notifier.Name);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Alert channel {Channel} failed for {Job} {Rule}: {Error}; retrying next cycle",
                        notifier.Name, incident.JobName, incident.Rule, ex.Message);
                }
            }
            incident.OpenSentTo = string.Join(',', openSent.Order());
            incident.ResolveSentTo = string.Join(',', resolveSent.Order());
            await _db.SaveChangesAsync(ct);
        }
        return sent;
    }

    public async Task<List<AlertChannelResult>> SendTestAsync(string? requestedBy, DateTime nowUtc, CancellationToken ct)
    {
        var message = new AlertMessage(
            "[CATO] test alert",
            $"🧪 CATO test alert, sent {Stamp(nowUtc)} UTC by {requestedBy ?? "the API"}.\n" +
            $"Both channels work if this arrives by e-mail and on Discord.\n{_settings.JobsPageUrl}");
        var results = new List<AlertChannelResult>();
        foreach (var notifier in _notifiers)
        {
            try
            {
                await notifier.SendAsync(message, ct);
                results.Add(new AlertChannelResult(notifier.Name, true, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new AlertChannelResult(notifier.Name, false, ex.Message));
            }
        }
        return results;
    }

    // ── messages ──

    private AlertMessage Opened(AlertIncident i) => new(
        $"[CATO] {i.JobName}: {AlertRules.Title(i.Rule)}",
        $"🔴 CATO alert: {i.JobName} - {AlertRules.Title(i.Rule)}\n{i.Message}\n" +
        $"Since {Stamp(i.OpenedAt)} UTC\n{_settings.JobsPageUrl}");

    private AlertMessage Resolved(AlertIncident i, DateTime nowUtc) => new(
        $"[CATO] resolved: {i.JobName}: {AlertRules.Title(i.Rule)}",
        $"✅ Resolved: {i.JobName} - {AlertRules.Title(i.Rule)}\n" +
        $"Open for {JobRunStatusEvaluator.FormatSpan((i.ResolvedAt ?? nowUtc) - i.OpenedAt)}, since {Stamp(i.OpenedAt)} UTC\n" +
        $"{_settings.JobsPageUrl}");

    private static HashSet<string> Channels(string list) =>
        list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

    private static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record Items(long Total, long Failed, string Unit);

    private static Items? ReadItems(string? metricsJson)
    {
        if (string.IsNullOrWhiteSpace(metricsJson))
            return null;
        using var doc = JsonDocument.Parse(metricsJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("items_total", out var total) || total.ValueKind != JsonValueKind.Number
            || !root.TryGetProperty("items_failed", out var failed) || failed.ValueKind != JsonValueKind.Number)
            return null;
        var unit = root.TryGetProperty("items_unit", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString() ?? "items"
            : "items";
        return new Items(total.GetInt64(), failed.GetInt64(), unit);
    }
}
