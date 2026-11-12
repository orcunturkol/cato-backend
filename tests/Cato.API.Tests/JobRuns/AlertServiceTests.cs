using Cato.API.Services.Alerts;
using Cato.API.Services.Credentials;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.JobRuns;

public class AlertServiceTests : JobRunsDbTestBase
{
    // Base clock: 2026-10-06 15:21 UTC; the API started at 14:17:40.

    private readonly FakeNotifier _email = new("sns");
    private readonly FakeNotifier _discord = new("discord");
    private readonly FakeQueues _queues = new();
    private readonly QueueProgressTracker _progress = new();
    private IJobFreshnessSource _freshness = new FakeFreshness([]);
    // A login with 200 days left: quiet unless a test changes it.
    private readonly FakeSteamworksLogin _steamworks = new() { Token = Login(Now.AddDays(200)) };

    private AlertService Service(CatoDbContext db) => new(
        db, NewCatalog(db), WorkerStart(db), [_email, _discord], _queues, _progress,
        Options.Create(new AlertSettings()), Options.Create(new RabbitMqSettings()), _freshness,
        _steamworks, NullLogger<AlertService>.Instance);

    private async Task<AlertCycleResult> CycleAsync(DateTime now)
    {
        await using var db = NewContext();
        return await Service(db).RunCycleAsync(now, default);
    }

    private List<AlertIncident> Incidents()
    {
        using var db = NewContext();
        return db.AlertIncidents.OrderBy(i => i.OpenedAt).ToList();
    }

    private void Add(params object[] rows)
    {
        using var db = NewContext();
        db.AddRange(rows);
        db.SaveChanges();
    }

    private static ScheduledJob Job(string name, string group = JobAgentGroups.Light, int timeoutSeconds = 3600,
        decimal ratio = 0.10m, int minItems = 1) => new()
    {
        Name = name, Label = name, AgentGroup = group, Module = $"orchestrators.run_{name}", Args = "",
        Cron = "0 */4 * * *", Enabled = true, TimeoutSeconds = timeoutSeconds, ManagedBy = ScheduledJobManagedBy.Dispatcher,
        AlertFailedRatio = ratio, AlertMinItems = minItems, UpdatedAt = Now.AddDays(-1),
    };

    private static JobRun Run(string job, DateTime start, string status, string? metrics = null, string? error = null,
        string producer = JobRunProducer.ExternalCollector) => new()
    {
        Id = Guid.NewGuid(), JobName = job, Producer = producer, StartTime = start,
        EndTime = status == JobRunStatus.Running ? null : start.AddMinutes(2), Status = status,
        MetricsJson = metrics, ErrorMessage = error, CreatedAt = start,
    };

    private static string Items(int total, int failed) =>
        $$"""{"items_total":{{total}},"items_failed":{{failed}},"items_unit":"games"}""";

    private static JobDispatch Dispatch(string job, string group, DateTime slot, string status) => new()
    {
        Id = Guid.NewGuid(), JobName = job, AgentGroup = group, ScheduledAt = slot,
        Kind = JobDispatchKind.Scheduled, Status = status, CreatedAt = slot,
    };

    [Fact]
    public async Task A_failed_run_alerts_once_per_channel_and_resolves_once_after_a_good_run()
    {
        Add(Job("ccu"), Run("ccu", Now.AddMinutes(-30), JobRunStatus.Failed, error: "redis unreachable: timeout"));

        var first = await CycleAsync(Now);
        var second = await CycleAsync(Now.AddMinutes(1));

        Assert.Equal((1, 2), (first.Opened, first.Sent));
        Assert.Equal((0, 0), (second.Opened, second.Sent));
        var incident = Assert.Single(Incidents());
        Assert.Equal((AlertRules.RunFailed, "ccu|run_failed"), (incident.Rule, incident.OpenKey));
        Assert.Contains("redis unreachable", _email.Sent.Single().Body);
        Assert.Equal("[CATO] ccu: run failed", _discord.Sent.Single().Subject);

        Add(Run("ccu", Now.AddMinutes(5), JobRunStatus.Succeeded, Items(549, 0)));
        var third = await CycleAsync(Now.AddMinutes(10));

        Assert.Equal((1, 2), (third.Resolved, third.Sent));
        incident = Assert.Single(Incidents());
        Assert.Null(incident.OpenKey);
        Assert.Equal(Now.AddMinutes(10), incident.ResolvedAt);
        Assert.Equal(("discord,sns", "discord,sns"), (incident.OpenSentTo, incident.ResolveSentTo));
        Assert.StartsWith("✅ Resolved: ccu - run failed", _email.Sent[1].Body);
    }

    [Fact]
    public async Task An_open_alert_is_repeated_once_a_day_until_it_resolves()
    {
        // 2026-10-30: one message for a stopped watcher, then six silent days.
        Add(Job("ccu"), Run("ccu", Now.AddMinutes(-30), JobRunStatus.Failed, error: "redis unreachable: timeout"));
        Assert.Equal(2, (await CycleAsync(Now)).Sent);

        Assert.Equal(0, (await CycleAsync(Now.AddHours(23))).Sent);
        Assert.Equal(2, (await CycleAsync(Now.AddHours(24))).Sent);
        Assert.Equal(0, (await CycleAsync(Now.AddHours(25))).Sent);
        Assert.Equal(2, (await CycleAsync(Now.AddHours(48))).Sent);

        var reminder = _email.Sent[1];
        Assert.Equal("[CATO] still open: ccu: run failed", reminder.Subject);
        Assert.Contains("redis unreachable", reminder.Body);
        Assert.Contains("Open for 24h", reminder.Body);

        Add(Run("ccu", Now.AddHours(49), JobRunStatus.Succeeded, Items(549, 0)));
        Assert.Equal(2, (await CycleAsync(Now.AddHours(49).AddMinutes(5))).Sent); // the resolved message
        Assert.Equal(0, (await CycleAsync(Now.AddHours(80))).Sent);
        Assert.Equal(["[CATO] ccu: run failed", "[CATO] still open: ccu: run failed",
            "[CATO] still open: ccu: run failed", "[CATO] resolved: ccu: run failed"], _email.Sent.Select(m => m.Subject));
    }

    [Fact]
    public async Task Skipped_and_interrupted_runs_neither_alert_nor_resolve()
    {
        Add(Job("steamdb_followers", JobAgentGroups.SteamDb),
            Run("steamdb_followers", Now.AddHours(-3), JobRunStatus.Failed, error: "aborted: session_failed"),
            Run("steamdb_followers", Now.AddHours(-2), JobRunStatus.Skipped),
            Run("steamdb_followers", Now.AddHours(-1), JobRunStatus.Interrupted));

        await CycleAsync(Now);

        Assert.Null(Assert.Single(Incidents()).ResolvedAt);
    }

    [Theory]
    [InlineData(4000, 2000, 0.10, true)]
    [InlineData(4000, 399, 0.10, false)]
    [InlineData(4000, 400, 0.10, true)]
    [InlineData(4000, 1, 0.10, false)]
    [InlineData(40, 20, 0.50, true)]
    [InlineData(40, 19, 0.50, false)]
    public async Task Failed_items_alert_at_the_jobs_threshold(int total, int failed, double ratio, bool alerts)
    {
        Add(Job("group_member_count", ratio: (decimal)ratio),
            Run("group_member_count", Now.AddHours(-1), JobRunStatus.PartialSuccess, Items(total, failed)));

        await CycleAsync(Now);

        Assert.Equal(alerts, Incidents().Any(i => i.Rule == AlertRules.ItemsFailed));
        Assert.DoesNotContain(Incidents(), i => i.Rule == AlertRules.RunFailed);
    }

    [Fact]
    public async Task A_lost_collector_run_alerts_but_a_watcher_run_lost_to_an_api_restart_does_not()
    {
        Add(Job("steamdb_followers", JobAgentGroups.SteamDb, timeoutSeconds: 3000),
            // No finish 3 h after the start: past the 50 min limit plus grace.
            Run("steamdb_followers", Now.AddHours(-3), JobRunStatus.Running),
            Run(BackendJobNames.GameEnrichmentWatcher, ApiStart.AddMinutes(-50), JobRunStatus.Succeeded,
                producer: JobRunProducer.CatoBackend),
            Run(BackendJobNames.GameEnrichmentWatcher, ApiStart.AddMinutes(-5), JobRunStatus.Running,
                producer: JobRunProducer.CatoBackend));

        await CycleAsync(Now);

        var incident = Assert.Single(Incidents());
        Assert.Equal(("steamdb_followers", AlertRules.RunFailed), (incident.JobName, incident.Rule));
        Assert.StartsWith("Lost: no finish reported", incident.Message);
    }

    [Fact]
    public async Task A_hung_watcher_alerts_but_one_lost_to_a_worker_restart_does_not()
    {
        Add(
            // PICS polls every 10 min, so no finish within 1 h means it hangs.
            Run(BackendJobNames.SteamPicsWatcher, ApiStart.AddMinutes(1), JobRunStatus.Running, producer: JobRunProducer.CatoBackend),
            // Started before the worker's start: ended by the restart.
            Run(BackendJobNames.GameNewsWatcher, ApiStart.AddMinutes(-10), JobRunStatus.Running, producer: JobRunProducer.CatoBackend));

        await CycleAsync(Now);

        var incident = Assert.Single(Incidents());
        Assert.Equal((BackendJobNames.SteamPicsWatcher, AlertRules.RunFailed), (incident.JobName, incident.Rule));
        Assert.StartsWith("Lost: no finish reported within 1h", incident.Message);
    }

    [Fact]
    public async Task A_watcher_run_with_failed_items_alerts()
    {
        // 2026-10-23: Valve switched off the review endpoint; the review sync failed for 516 of
        // 709 games and stored PartialSuccess, but without item counts nothing alerted.
        Add(Run(BackendJobNames.SteamReviewWatcher, Now.AddMinutes(-20), JobRunStatus.PartialSuccess,
            Items(709, 516), producer: JobRunProducer.CatoBackend));

        await CycleAsync(Now);

        var incident = Assert.Single(Incidents());
        Assert.Equal((BackendJobNames.SteamReviewWatcher, AlertRules.ItemsFailed), (incident.JobName, incident.Rule));
        Assert.StartsWith("516 of 709 games failed", incident.Message);
    }

    [Fact]
    public async Task A_failed_watcher_run_alerts()
    {
        Add(Run(BackendJobNames.GameEnrichmentWatcher, Now.AddMinutes(-20), JobRunStatus.Failed,
            error: "Exception while reading from stream", producer: JobRunProducer.CatoBackend));

        await CycleAsync(Now);

        Assert.Equal(BackendJobNames.GameEnrichmentWatcher, Assert.Single(Incidents()).JobName);
    }

    private static ScheduledJob Watcher(string name, int intervalMinutes, bool enabled = true) => new()
    {
        Name = name, Label = name, AgentGroup = JobAgentGroups.Worker, Module = "", Args = "", Cron = null,
        Enabled = enabled, TimeoutSeconds = 0, ManagedBy = ScheduledJobManagedBy.Worker,
        IntervalMinutes = intervalMinutes, UpdatedAt = Now.AddDays(-1),
    };

    [Fact]
    public async Task A_watcher_that_stopped_running_alerts_and_resolves_when_it_runs_again()
    {
        // 2026-10-30: one Steam timeout ended the loop; the last run ended 14:24:40, the worker runs on.
        Add(Watcher(BackendJobNames.PlayerAchievementWatcher, 10),
            Run(BackendJobNames.PlayerAchievementWatcher, ApiStart.AddMinutes(5), JobRunStatus.Succeeded,
                producer: JobRunProducer.CatoBackend));

        await CycleAsync(Now); // 56 min after its last run: inside 2 x 10 min + 1 h
        Assert.Empty(Incidents());

        await CycleAsync(Now.AddHours(1));
        var incident = Assert.Single(Incidents());
        Assert.Equal((BackendJobNames.PlayerAchievementWatcher, AlertRules.WatcherStopped), (incident.JobName, incident.Rule));
        Assert.StartsWith("no run for 1 h 56 min (it runs every 10 min); the last run started", incident.Message);

        Add(Run(BackendJobNames.PlayerAchievementWatcher, Now.AddHours(1).AddMinutes(1), JobRunStatus.Succeeded,
            producer: JobRunProducer.CatoBackend));
        await CycleAsync(Now.AddHours(1).AddMinutes(5));
        Assert.NotNull(Assert.Single(Incidents()).ResolvedAt);
    }

    [Fact]
    public async Task A_running_paused_or_recent_watcher_does_not_alert()
    {
        Add(
            // Ran 3 h ago, every 6 h: inside its window.
            Watcher(BackendJobNames.GameNewsWatcher, 360),
            Run(BackendJobNames.GameNewsWatcher, Now.AddHours(-3), JobRunStatus.Succeeded, producer: JobRunProducer.CatoBackend),
            // A long cycle in progress.
            Watcher(BackendJobNames.SteamReviewWatcher, 60),
            Run(BackendJobNames.SteamReviewWatcher, Now.AddHours(-6), JobRunStatus.Succeeded, producer: JobRunProducer.CatoBackend),
            Run(BackendJobNames.SteamReviewWatcher, Now.AddMinutes(-20), JobRunStatus.Running, producer: JobRunProducer.CatoBackend),
            // Paused on the Jobs page.
            Watcher(BackendJobNames.SteamPlayerProfileWatcher, 30, enabled: false),
            Run(BackendJobNames.SteamPlayerProfileWatcher, Now.AddDays(-1), JobRunStatus.Succeeded, producer: JobRunProducer.CatoBackend));

        await CycleAsync(Now);

        Assert.Empty(Incidents());
    }

    [Fact]
    public async Task A_dispatch_nobody_starts_is_a_missed_run_unless_its_group_is_busy()
    {
        Add(Job("ccu"), Job("steamworks_navstats", JobAgentGroups.Steamworks),
            Dispatch("ccu", JobAgentGroups.Light, Now.AddMinutes(-6), JobDispatchStatus.Pending),
            // Navstats waits behind a long traffic run: that is the queue working.
            Dispatch("steamworks_navstats", JobAgentGroups.Steamworks, Now.AddMinutes(-20), JobDispatchStatus.Pending),
            Dispatch("steamworks_traffic", JobAgentGroups.Steamworks, Now.AddMinutes(-50), JobDispatchStatus.Claimed));

        await CycleAsync(Now);

        var incident = Assert.Single(Incidents());
        Assert.Equal(("ccu", AlertRules.MissedRun), (incident.JobName, incident.Rule));
        Assert.Contains("has not started after 6 min", incident.Message);

        await using (var db = NewContext())
        {
            db.JobDispatches.Single(d => d.JobName == "ccu").Status = JobDispatchStatus.Claimed;
            await db.SaveChangesAsync();
        }
        await CycleAsync(Now.AddMinutes(1));

        Assert.NotNull(Assert.Single(Incidents()).ResolvedAt);
    }

    [Fact]
    public async Task An_expired_dispatch_is_a_missed_run()
    {
        Add(Job("financial"), Dispatch("financial", JobAgentGroups.Light, Now.AddHours(-7), JobDispatchStatus.Expired));

        await CycleAsync(Now);

        Assert.Contains("expired", Assert.Single(Incidents()).Message);
    }

    [Fact]
    public async Task A_silent_agent_alerts_and_resolves_when_it_calls_again()
    {
        Add(new JobAgent
        {
            AgentId = "steamdb@host", AgentGroup = JobAgentGroups.SteamDb, Host = "host",
            FirstSeenAt = Now.AddDays(-1), LastSeenAt = Now.AddMinutes(-11),
        });

        await CycleAsync(Now);
        Assert.Equal("agent:steamdb", Assert.Single(Incidents()).JobName);

        await using (var db = NewContext())
        {
            db.JobAgents.Single().LastSeenAt = Now.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        await CycleAsync(Now.AddMinutes(1));

        Assert.NotNull(Assert.Single(Incidents()).ResolvedAt);
    }

    private static JobAgent Agent(string group, string host, DateTime lastSeen) => new()
    {
        AgentId = $"{group}@{host}", AgentGroup = group, Host = host,
        FirstSeenAt = Now.AddDays(-1), LastSeenAt = lastSeen,
    };

    [Fact]
    public async Task A_replaced_agent_does_not_alert_while_its_group_calls()
    {
        // A new ECS task has a new host: the old id stays in the table, silent.
        Add(Agent(JobAgentGroups.SteamDb, "ip-old", Now.AddHours(-2)), Agent(JobAgentGroups.SteamDb, "ip-new", Now.AddMinutes(-1)));

        await CycleAsync(Now);

        Assert.Empty(Incidents());
    }

    [Fact]
    public async Task A_silent_group_opens_one_incident()
    {
        Add(Agent(JobAgentGroups.SteamDb, "ip-old", Now.AddHours(-2)), Agent(JobAgentGroups.SteamDb, "ip-new", Now.AddMinutes(-12)),
            Agent(JobAgentGroups.Light, "ip-light", Now.AddMinutes(-1)));

        await CycleAsync(Now);

        var incident = Assert.Single(Incidents());
        Assert.Equal(("agent:steamdb", "agent:steamdb|agent_offline"), (incident.JobName, incident.OpenKey));
        Assert.Contains("steamdb@ip-new", incident.Message);
    }

    [Fact]
    public async Task An_incident_keyed_by_agent_id_resolves()
    {
        Add(Agent(JobAgentGroups.SteamDb, "ip-new", Now.AddMinutes(-1)), new AlertIncident
        {
            Id = Guid.NewGuid(), JobName = "agent:steamdb@ip-old", Rule = AlertRules.AgentOffline,
            OpenKey = "agent:steamdb@ip-old|agent_offline", Message = "silent", OpenedAt = Now.AddHours(-1),
            LastSeenAt = Now.AddMinutes(-1), OpenSentTo = "discord,sns",
        });

        await CycleAsync(Now);

        var incident = Assert.Single(Incidents());
        Assert.Equal((Now, null), (incident.ResolvedAt, incident.OpenKey));
        Assert.Equal("discord,sns", incident.ResolveSentTo);
    }

    [Fact]
    public async Task Rejected_batches_and_a_queue_without_progress_alert()
    {
        _queues.Stats = [new("cato-ingestion-dlq", 3, 0), new("cato-ingestion-v2", 5, 1)];
        await CycleAsync(Now);
        Assert.Equal("queue:cato-ingestion-dlq", Assert.Single(Incidents()).JobName);

        await CycleAsync(Now.AddMinutes(31));
        Assert.Contains(Incidents(), i => i.JobName == "queue:cato-ingestion-v2" && i.Rule == AlertRules.QueueStuck);

        _queues.Stats = [new("cato-ingestion-dlq", 0, 0), new("cato-ingestion-v2", 4, 1)];
        await CycleAsync(Now.AddMinutes(32));
        Assert.All(Incidents(), i => Assert.NotNull(i.ResolvedAt));
    }

    [Fact]
    public async Task A_broker_that_cannot_be_read_decides_nothing()
    {
        _queues.Fail = true;

        var result = await CycleAsync(Now);

        Assert.Equal(1, result.Checks); // the Steamworks login check only
    }

    [Fact]
    public async Task A_failed_channel_is_retried_alone()
    {
        Add(Job("ccu"), Run("ccu", Now.AddMinutes(-30), JobRunStatus.Failed, error: "boom"));
        _discord.FailNext = 1;

        await CycleAsync(Now);
        await CycleAsync(Now.AddMinutes(1));

        Assert.Single(_email.Sent);
        Assert.Single(_discord.Sent);
        Assert.Equal("discord,sns", Assert.Single(Incidents()).OpenSentTo);
    }

    [Fact]
    public async Task An_incident_resolved_before_any_message_went_out_sends_nothing()
    {
        Add(Job("ccu"), Run("ccu", Now.AddMinutes(-30), JobRunStatus.Failed, error: "boom"));
        _email.FailNext = _discord.FailNext = 1;
        await CycleAsync(Now);

        Add(Run("ccu", Now.AddMinutes(2), JobRunStatus.Succeeded));
        await CycleAsync(Now.AddMinutes(5));

        Assert.Empty(_email.Sent);
        Assert.Empty(_discord.Sent);
    }

    [Fact]
    public async Task The_test_message_reports_each_channel()
    {
        _discord.FailNext = 1;
        await using var db = NewContext();

        var results = await Service(db).SendTestAsync("orcun", Now, default);

        Assert.Equal([("sns", true), ("discord", false)], results.Select(r => (r.Channel, r.Sent)));
        Assert.Contains("by orcun", _email.Sent.Single().Body);
    }

    [Fact]
    public void Email_subjects_stay_ascii_and_short()
    {
        var subject = AlertText.Clip(AlertText.Ascii("[CATO] " + new string('x', 120) + " ✅\n"), 99, "...");

        Assert.Equal(99, subject.Length);
        Assert.All(subject, c => Assert.InRange(c, ' ', '~'));
    }

    private sealed class FakeNotifier(string name) : IAlertNotifier
    {
        public List<AlertMessage> Sent { get; } = [];
        public int FailNext { get; set; }
        public string Name => name;

        public Task SendAsync(AlertMessage message, CancellationToken ct)
        {
            if (FailNext > 0)
            {
                FailNext--;
                throw new HttpRequestException($"{name} is down");
            }
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Stale_data_of_an_enabled_job_alerts_and_fresh_data_resolves_it()
    {
        var stale = new JobFreshnessDto("ccu", "external-collector", "ccu_history", "Timestamp",
            Now.AddHours(-6), 4.5, 6, Stale: true, Enabled: true, Error: null);
        _freshness = new FakeFreshness([stale, stale with { JobName = "financial", Enabled = false, Stale = false }]);
        await CycleAsync(Now);

        var incident = Assert.Single(Incidents());
        Assert.Equal(("ccu", AlertRules.StaleData), (incident.JobName, incident.Rule));
        Assert.Contains("6 h old; the limit is 4.5 h", incident.Message);

        _freshness = new FakeFreshness([stale with { NewestAt = Now, AgeHours = 0, Stale = false }]);
        await CycleAsync(Now.AddMinutes(10));
        Assert.NotNull(Assert.Single(Incidents()).ResolvedAt);
    }

    [Fact]
    public async Task A_stale_data_incident_of_a_job_without_a_check_resolves()
    {
        var stale = new JobFreshnessDto("steamdb_followers", "external-collector", "group_member_count_snapshot",
            "SnapshotDate (SteamDB followers)", Now.AddHours(-56), 48, 56, Stale: true, Enabled: true, Error: null);
        _freshness = new FakeFreshness([stale]);
        await CycleAsync(Now);
        Assert.Null(Assert.Single(Incidents()).ResolvedAt);

        // The check was removed: the open incident must not stay open for ever.
        _freshness = new FakeFreshness([]);
        await CycleAsync(Now.AddMinutes(10));
        Assert.NotNull(Assert.Single(Incidents()).ResolvedAt);
    }

    private static SteamLoginToken Login(DateTime expiresAt) =>
        new("76561198000008567", ["web", "renew", "derive"], expiresAt.AddDays(-207), expiresAt);

    private List<AlertIncident> SteamworksIncidents() =>
        Incidents().Where(i => i.Rule == AlertRules.SteamworksLoginExpiring).ToList();

    [Fact]
    public async Task A_steamworks_login_close_to_its_expiry_alerts_with_how_to_renew()
    {
        _steamworks.Token = Login(Now.AddDays(13.5));

        var result = await CycleAsync(Now);

        Assert.Equal((1, 2), (result.Opened, result.Sent));
        var incident = Assert.Single(SteamworksIncidents());
        Assert.Equal("steamworks_login|steamworks_login_expiring", incident.OpenKey);
        Assert.Contains("account …8567 expires on 2026-10-20 03:21 UTC, in 13 day(s)", incident.Message);
        Assert.Contains("PUT /api/credentials/steamworks/refresh-cookie", incident.Message);
        Assert.Contains("--set-refresh-cookie", incident.Message);
        Assert.Equal("[CATO] steamworks_login: Steamworks login expiring", _email.Sent.Single().Subject);
    }

    [Fact]
    public async Task An_expired_or_missing_steamworks_login_alerts()
    {
        _steamworks.Token = Login(Now.AddDays(-1));
        await CycleAsync(Now);
        Assert.Contains("expired on 2026-10-05 15:21 UTC", Assert.Single(SteamworksIncidents()).Message);

        _steamworks.Token = null;
        await CycleAsync(Now.AddMinutes(1));
        var incident = Assert.Single(SteamworksIncidents());
        Assert.Null(incident.ResolvedAt);
        Assert.StartsWith("No readable Steamworks login is stored", incident.Message);
    }

    [Fact]
    public async Task A_steamworks_login_with_enough_days_left_is_quiet()
    {
        _steamworks.Token = Login(Now.AddDays(14.5));

        var result = await CycleAsync(Now);

        Assert.Equal((1, 0, 0), (result.Checks, result.Opened, result.Sent));
        Assert.Empty(Incidents());
    }

    [Fact]
    public async Task A_renewed_steamworks_login_resolves_the_alert()
    {
        _steamworks.Token = Login(Now.AddDays(3));
        await CycleAsync(Now);

        _steamworks.Token = Login(Now.AddDays(207));
        var result = await CycleAsync(Now.AddMinutes(30));

        Assert.Equal((1, 2), (result.Resolved, result.Sent));
        Assert.NotNull(Assert.Single(SteamworksIncidents()).ResolvedAt);
    }

    [Fact]
    public async Task A_steamworks_login_that_cannot_be_read_keeps_the_open_alert_and_sends_nothing()
    {
        _steamworks.Token = Login(Now.AddDays(3));
        await CycleAsync(Now);
        Assert.Equal(2, _email.Sent.Count + _discord.Sent.Count);

        _steamworks.Failed = true;
        var result = await CycleAsync(Now.AddMinutes(30));

        Assert.Equal((0, 0, 0, 0), (result.Checks, result.Opened, result.Resolved, result.Sent));
        var incident = Assert.Single(SteamworksIncidents());
        Assert.Null(incident.ResolvedAt);
        Assert.Contains("in 3 day(s)", incident.Message);
    }

    [Fact]
    public async Task The_steamworks_login_cache_reads_the_store_once_per_max_age_and_keeps_a_failure()
    {
        var store = new CountingStore();
        var service = new SteamworksLoginService(store,
            Options.Create(new Cato.Infrastructure.Secrets.SecretStoreSettings()), TimeProvider.System,
            NullLogger<SteamworksLoginService>.Instance, Options.Create(new AlertSettings()));
        var cache = new SteamworksLoginCache(NullLogger<SteamworksLoginCache>.Instance);

        store.Fail = true;
        Assert.True((await cache.GetAsync(service, Now, default)).Failed);
        Assert.True((await cache.GetAsync(service, Now.AddMinutes(29), default)).Failed);
        Assert.Equal(1, store.Reads);

        store.Fail = false;
        var read = await cache.GetAsync(service, Now.AddMinutes(30), default);
        Assert.Equal((false, null, 2), (read.Failed, read.Token, store.Reads));
    }

    private sealed class FakeSteamworksLogin : ISteamworksLoginSource
    {
        public SteamLoginToken? Token { get; set; }
        public bool Failed { get; set; }

        public Task<SteamworksLoginRead> GetAsync(DateTime nowUtc, CancellationToken ct) =>
            Task.FromResult(new SteamworksLoginRead(Failed ? null : Token, Failed));
    }

    private sealed class CountingStore : Cato.Infrastructure.Secrets.ISecretStore
    {
        public int Reads { get; private set; }
        public bool Fail { get; set; }

        public Task<string?> GetAsync(string id, CancellationToken ct)
        {
            Reads++;
            return Fail ? throw new HttpRequestException("AccessDenied") : Task.FromResult<string?>(null);
        }

        public Task PutAsync(string id, string value, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeFreshness(List<JobFreshnessDto> results) : IJobFreshnessSource
    {
        public Task<(DateTime CheckedAt, List<JobFreshnessDto> Results)> GetAsync(DateTime nowUtc, CancellationToken ct) =>
            Task.FromResult((nowUtc, results));
    }

    private sealed class FakeQueues : IQueueStatsReader
    {
        public IReadOnlyList<QueueStats> Stats { get; set; } = [];
        public bool Fail { get; set; }

        public Task<IReadOnlyList<QueueStats>> ReadAsync(CancellationToken ct) =>
            Fail ? throw new InvalidOperationException("broker down") : Task.FromResult(Stats);
    }
}
