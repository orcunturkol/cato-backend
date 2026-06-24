# Observability: job-run tracking, logs, and alerting

This system tracks **what every job did** (succeeded / failed / partial, with
per-job counters), makes logs **searchable by time** in Seq, and **alerts to
Discord** when a job fails. It spans both repos:

- **cato-backend** — the 11 background watchers (achievements, schema, reviews,
  prices, profiles, PICS) each record a row in the `job_run` table per cycle and
  emit a structured `JobRun … finished` log event.
- **catoptric-data-collector** — each orchestrator run (`steamdb`, `ccu`,
  `financial`, `group_member_count`) reports a `job_run` via the backend HTTP API
  and ships its structlog events to Seq.

## Components

| Concern | Where | Notes |
|---|---|---|
| Run history (queryable) | `job_run` table | Generalizes `ingestion_log`. Per-job counters in the `MetricsJson` jsonb column. |
| Log search by time/property | **Seq** (`docker-compose.yml` → `seq`) | UI + ingestion at `http://<host>:5341`. |
| Failure alerts | Seq **Signal → webhook → Discord** | Configured in the Seq UI (below). No app code. |
| Log hygiene | `appsettings.json` → `Serilog` section | EF SQL / HTTP-handler noise suppressed to Warning. |

## job_run API

- `POST /api/job-runs` — report a completed run. Body:
  `{ jobName, producer, startTime, endTime, status, metrics, errorMessage }`.
  `status` ∈ `Running | Succeeded | PartialSuccess | Failed`. `metrics` is an
  arbitrary JSON object. Used by the Python collector (`CATO_API_URL`).
- `GET /api/job-runs?jobName=&status=&limit=` — list recent runs (newest first).
  e.g. `GET /api/job-runs?status=Failed` to see everything that broke.

The .NET watchers write `job_run` rows directly via `IJobRunTracker`
(`src/Cato.Infrastructure/Jobs/`), so they don't go through the HTTP API.

## Running Seq

```bash
docker compose up -d seq
# Open the UI:  http://localhost:5341   (or http://<TAILSCALE_IP>:5341)
```

The API ships logs to Seq automatically:
- locally via `appsettings.json` (`serverUrl: http://localhost:5341`);
- in compose via the `Serilog__WriteTo__1__Args__serverUrl: http://seq:80` env override.

For the Python collector, set `SEQ_URL` (e.g. `http://localhost:5341`) and
`CATO_API_URL` (e.g. `http://localhost:5039`) in the cron/.env environment.

First run: open Seq, set an admin password (or bake a hash via
`SEQ_FIRSTRUN_ADMINPASSWORDHASH`, generated with
`docker run --rm datalust/seq config hash`).

## Discord failure alerts (Seq UI, no code)

1. **Install the webhook app**: Seq → *Settings → Apps → Install from NuGet* →
   `Seq.App.Http` (HTTP/webhook notifier).
2. **Create a Signal** that matches failures: Seq → *Signals → Add Signal*, with
   a filter such as:
   ```
   @Level = 'Error' or Status = 'Failed'
   ```
   (`Status`, `JobName`, `Producer`, `DurationMs` are first-class properties on
   the `JobRun … finished` events; failures are logged at `Error`.)
3. **Add an alert/notification**: create a webhook instance of the app, target
   the **Discord webhook URL**, and set the message body template, e.g.:
   ```json
   { "content": "🔴 {{JobName}} {{Status}} ({{Service}}) — {{ErrorMessage}}" }
   ```
   Bind it to the signal from step 2. Throttle/group so a burst of failures
   doesn't spam the channel.
4. Keep the Discord webhook URL in Seq settings only — do not commit it.

### Test the alert
Post a synthetic failed run and confirm Discord fires:
```bash
curl -X POST http://localhost:5039/api/job-runs -H 'Content-Type: application/json' -d '{
  "jobName":"steamdb","producer":"external-collector",
  "startTime":"2026-06-17T13:00:00Z","endTime":"2026-06-17T13:01:00Z",
  "status":"Failed","metrics":{"source":"steamdb_most_wished"},
  "errorMessage":"synthetic test failure"
}'
```
The row appears in `GET /api/job-runs?status=Failed`; the `Error`-level event
appears in Seq and triggers the signal → Discord.
