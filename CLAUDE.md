# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CATO is a Steam game analytics backend that ingests and serves financial, traffic, CCU, ownership, and ranking data for Steam games. It integrates with the Steam API and SteamKit2 for real-time data, and uses RabbitMQ for async ingestion pipelines.

## Build & Run Commands

```bash
# Build
dotnet build

# Run the API (requires RabbitMQ from docker-compose; Postgres is AWS RDS — see infra/rds/)
docker compose up -d rabbitmq
dotnet run --project src/Cato.API

# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project src/Cato.Infrastructure --startup-project src/Cato.API
dotnet ef database update --project src/Cato.Infrastructure --startup-project src/Cato.API
```

Role `all` (a plain `dotnet run`) auto-migrates on startup. In compose the one-shot
`migrate` service applies the migrations, and `api` and `worker` start after it exits 0.

```bash
dotnet test                                          # all test projects
dotnet test tests/Cato.API.Tests                     # ingestion handlers
```

Test projects: `tests/Cato.Infrastructure.Tests` (SteamKit reconnect policy) and
`tests/Cato.API.Tests` (ingestion handlers, on SQLite in-memory so unique indexes
are genuinely enforced). Running them needs the **ASP.NET Core shared runtime**
(`aspnet-runtime-10.0`), not just `dotnet-runtime` — `Cato.API.Tests` references
the Web-SDK API project, so the test host fails to launch without it.

## Architecture

**.NET 10** solution with three projects:

- **Cato.API** — ASP.NET Core Web API. Controllers, MediatR handlers, DTOs, and application services. The entry point and DI composition root.
- **Cato.Domain** — Pure entity classes (no dependencies). All entities live in `Entities/`.
- **Cato.Infrastructure** — EF Core DbContext, Steam API/SteamKit2 clients, RabbitMQ messaging. References Domain only.

### Request Handling Pattern

Uses **MediatR** with command/query objects in `Models/{Feature}/` and handlers in `Services/Handlers/{Feature}/`. Feature groups: `Games`, `Ingestion`, `SteamDb`. FluentValidation is registered for request validation.

Some older services (`GameService`, `GameDataService`, `IngestionService`, `SteamKitDataService`) still exist as direct service classes in `Services/` — the project is migrating toward MediatR handlers.

### Key Infrastructure

- **Database**: PostgreSQL via EF Core (Npgsql), hosted on AWS RDS. Terraform for the RDS instance lives in `infra/rds/` (state in S3 since Phase 2 prep: `imports.tf` adopts the live database); see `infra/rds/CUTOVER_RUNBOOK.md` for provisioning, data migration, rollback, and cleanup procedures. On the server the connection string comes from the Secrets Manager secret `cato/backend/config` (see Configuration); the `ConnectionStrings__DefaultConnection` line in docker-compose only holds a development placeholder there. All entity table mappings and indexes are configured via Fluent API in `CatoDbContext.OnModelCreating`. Timestamps (CreatedAt/UpdatedAt) are set automatically in `SaveChanges`/`SaveChangesAsync`.
- **Messaging**: RabbitMQ with `IngestionDispatcher` (producer) and `RabbitMqConsumerService` (hosted service consumer). Exchange: `cato-data` (topic). Queues:
  - `cato-ingestion` — legacy single-item path, routing key `ingestion.*` → `IIngestionDispatcher`
  - four batch queues → `IBatchIngestionDispatcher`, each source on an explicit key `ingestion.batch.<source>` (`Messaging/BatchQueues.cs`): `cato-ingestion-ccu` (CCU, every 4 h), `cato-ingestion-followers` (SteamDB follower series, hourly), `cato-ingestion-steamworks` (the seven full-history `steamworks_*` sources) and `cato-ingestion-v2` (group members, SteamDB lists, financial). A slow source holds up only its own queue. **A new source needs a handler in `BatchIngestionDispatcher.Handlers` and a queue in `BatchQueues`** (a test checks that the lists match); the collector publishes with `mandatory=True`, so a source without a binding fails as unroutable instead of disappearing. The start-up binds every key before it removes the old wildcard `ingestion.batch.#` from v2. To roll back to a build with the wildcard, delete the three new queues first, or they collect copies with no consumer.
  - `cato-game-analyzed` — routing key `game.analyzed.#` → `IGameAnalyzedDispatcher`. Published by the **reddit_metrics** pipeline when Gemini extracts metrics for a game. Stubs the game if unknown, stamps `AnalyzedAt`, then moves its app ID to the front of the follower-history queue. Database and Redis work only — see the enrichment section for why nothing slow may go in here.

  **Each queue has its own channel** on one connection, with a dispatch concurrency of
  1 and a prefetch of 1, so a slow handler blocks only its own queue. Until 2026-10 the
  three shared one channel, and an inline Steam call in `IngestionService` froze
  `cato-game-analyzed` at 1829 messages and 0/min while `cato-ingestion-v2` crawled at
  one app per 15s. Keep every handler fast anyway; anything slow belongs in a
  background service. The queues now run concurrently, so two queues can stub the same
  unknown game at once.

  On shutdown a cancelled handler is neither acked nor nacked: the broker redelivers
  the message. The connection recovers its queues and consumers after a broker
  restart. `RabbitMQ:Uri` (`amqps://user:pass@host:5671/`, URL-encoded) overrides the
  host settings and turns TLS on; the connection is named `cato-{role}`.

  **Redis writes belong after `SaveChangesAsync`, never inside a transaction** (`GameService.cs`, `SteamGameEnrichmentService.cs`). This is why `game.analyzed` has its own queue rather than riding the batch path — `BatchIngestionDispatcher` runs every item handler inside one transaction, and a Redis write there would not roll back with it.
- **Steam Integration**: `SteamApiService` (HTTP client for Steam Web API) and `SteamKitService` (SteamKit2 for PICS change monitoring via `SteamPicsWatcherService` background service).
  - **SteamKit Game Discovery**: `SteamPicsWatcherService` polls Steam's PICS change feed via `PICSGetChangesSince`, which returns all AppIDs with any metadata change since the last known change number (persisted in `pics_change_number.txt`). For each changed AppID, it calls `PICSGetProductInfo` and reads the `common` KeyValue section. It filters by `common["type"] == "game"` (excludes DLCs, tools, demos) and `common["releasestate"] == "released"` (excludes unreleased). Also extracts `common["name"]` and `common["steam_release_date"]` (unix timestamp). Matching games are saved with `GameType = "Sourcing"`.
- **Price watcher**: `SteamPriceWatcherService` reads about 33,000 store prices per cycle (about
  18 h behind the shared Steam throttle). It saves every 200 games and keeps the last saved
  app id in `worker_state` (`price_watcher_last_app_id`, 0 when a cycle is complete), so a
  restart goes on from there; a snapshot's `CapturedAt` is when its price was read.
- **Logging**: Serilog, levels from the `Serilog` section. The console sink is always on;
  the Seq sink only when `Seq:ServerUrl` is set (compose: `http://seq:80`). Health probes
  and the agents' `POST /api/job-agents/*` polls log at Verbose unless they fail.
  `Microsoft.EntityFrameworkCore.Database.Command` is at `Fatal`: its "Failed executing
  DbCommand" error carries the whole SQL batch. From 2026-08-22 to 2026-10-04 one
  duplicate genre (app 2885250) poisoned the enrichment watcher's DbContext, so every
  later save of each 200-game cycle failed and re-sent the earlier rows: about 40 times
  the normal log volume (Seq: ~390 MB/day against ~10). The docker log reached 62 GB and
  filled the disk. The exception itself still logs (`Microsoft.EntityFrameworkCore.Update`
  and `.Query`, and the app's own handlers).
- **Swagger**: `/swagger` when `Swagger:Enabled` is true (Development, and compose on the server).

### Database Entities

Core entity is `Game` (table: `main_game`, keyed by `AppId`). Related time-series/snapshot entities: `SteamSaleFinancial`, `SteamTraffic`, `CcuHistory`, `OwnedGameData`, `GroupMemberCountSnapshot`, `SteamDbSnapshot`, `IngestionLog`. Games have `GameGenre` and `GenreTag` collections. `LegalEntity` represents developers/publishers.

`Services/DbTables/DbTableCatalog.cs` documents every table's data source, validation
and fallback; `GET /api/db-tables` serves it with live row counts to the frontend's
Database Tables page, and `GET /api/db-tables/export.csv` gives the same in the layout of
the "DB-Tables&Content" sheet. Columns, types and unique keys come from the EF model, so
only the lineage text is hand-written. **A new table fails `DbTablesTests` until it has a
catalog entry; a changed writer, source or fallback needs its entry updated by hand.**

### Process roles (`CATO_ROLE`)

One image runs as `all` (a plain `dotnet run`), `api` (HTTP only), `worker` (the
hosted services: watchers, consumers, dispatcher, alerts, SteamKit) or `migrate` (applies
migrations and exits). `[WorkerOnly]` controllers need the worker (the SteamKit login):
the api role does not map them, the worker maps only them. Store enrichment runs in the
worker, because it shares the static Steam throttle with the watchers:
`GameService.EnrichGameFromSteamAsync` (used by single enrich, by-appid enrich for OGT,
re-enrich, bulk import and owned-sync) calls `IGameEnrichmentGateway`. In role all or
worker that is `LocalGameEnrichmentGateway`; in role api it is
`RemoteGameEnrichmentGateway`, which posts to the worker's
`POST /api/internal/enrichment/games/{id}` (`Worker:BaseUrl`, 90 s). The answers keep
OGT's contract: 200 enriched, 404 unknown game, 400 failed; a worker that is down is an
error, not a quiet failure.

Compose runs the split since 2026-10-21 (PR 15): `migrate` (one-shot), `api` (`cato-api`,
`127.0.0.1:5039`, 1.5 GB) and `worker` (`cato-worker`, no published port, 3 GB), one image
`cato-backend:local`. Deploy with `docker compose up -d --build api worker`: compose runs
`migrate` first. Read both logs (`docker compose logs api worker`); the watchers, consumers,
dispatcher, alerts and SteamKit log in `worker`. The collector's data folder is no longer
mounted: every job publishes its data inline (only the unscheduled legacy `run_daily.py`
sent file paths).

### Steam store enrichment

`SteamGameEnrichmentService.EnrichGameAsync` is the single writer of a game's store
record: name, descriptions, media, price, platforms, release date, developer,
publisher, `GameGenre` rows, and `GenreTag` rows (store categories as `Mechanic`,
scraped user tags as `UserTag` with their rank as `Weight`). Two things reach it:

| Trigger | When |
|---|---|
| `SteamPicsWatcherService` | a newly discovered app, immediately after creation |
| `GameEnrichmentWatcherService` | hourly sweep — every game that has none |

The sweep (`GameEnrichment` settings section) works five bands in order:

1. reddit-analysed games with no store record (`AnalyzedAt != null`, newest first)
2. reddit-analysed games whose record has gone stale
3. everything else with no store record, by app id
4. everything else that has gone stale, stalest first
5. stale games past `FailureThreshold` (mostly delisted apps), stalest first

Bands 2, 4 and 5 need `RefreshAfterDays > 0`. Band 5 exists because a delisted app keeps
its old `LastEnrichedAt`: until 2026-10-21, 199 of them were the stalest records and took
199 of the 200 slots of every cycle, so the other ~62,000 stale games never refreshed. **Both reddit bands come before either
general band** — on plain "never enriched first" ordering a stale reddit game sits
behind ~9k discovered ones and would not be reached for two days.

`LastEnrichedAt` is the "done" marker and `EnrichmentFailures` the retry budget:
delisted and region-locked apps answer `success=false` forever, so past
`FailureThreshold` they are dropped from the never-enriched bands.

Three traps worth knowing:

- **Never enrich from inside a queue consumer.** `GameAnalyzedDispatcher` used to
  enrich inline for a game it had just stubbed, which reads as obviously correct and
  is not. `SteamApiService` throttles every Steam call through one **static**
  `SemaphoreSlim` shared by the whole process, and `cato-game-analyzed` is consumed
  serially with a prefetch of one. So while the enrichment watcher held that throttle
  for its 200-game cycle, a 1310-message backfill drained at **six messages per ten
  minutes** — a ~50-hour queue, with nothing in the logs but healthy-looking progress
  from the watcher. The consumer's job is the row, `AnalyzedAt`, and the Redis
  priority; `AnalyzedAt` is the handoff, and the watcher does the slow part.

- **Do not run the post-enrichment quality filter on reddit games.**
  `SteamPicsWatcherService.ApplyPostEnrichmentFilterAsync` *deletes* the row it
  rejects. That is right for an app PICS happened to surface, and wrong for a game
  reddit_metrics holds extractions and follower history for — which is why
  `GameEnrichmentWatcherService` enriches without it.
- **`LastEnrichedAt` is only as good as what seeded it.** For rows that predate the
  column it was backfilled from `UpdatedAt`, which means "last modified by anything"
  — and the price watcher touches games daily. So a game whose store record is a year
  old can look freshly enriched, and `RefreshAfterDays` will never find it: measured,
  a 30-day window caught 426 of 721 known-stale games. To repair records written by
  an older enrichment build, key off a column that build could not have written
  (`ReleaseDateRaw IS NULL` is the current marker) and clear `LastEnrichedAt` so the
  first band picks them up. `RefreshAfterDays` is for genuine drift, not for repair.

- **`ReleaseDate` is null for most unreleased games and that is correct.** Steam
  answers "Q4 2026", "2027" or "To be announced" for them; no calendar date exists.
  `ReleaseDateRaw` keeps the store's literal string, and `IsReleased` carries
  `coming_soon`. Query those two before concluding a release date is missing.

### Steam sales (`steam_sale_financial`)

`run_daily_financial` sends one batch item per app per changed date (`GetDetailedSales`
lines grouped by primary app, plus `sales_date`). Steam sends **several lines per
(date, country, package, platform)** — standalone vs bundle, two prices, a discount
change — about a third of all lines. So the table has no business key:
`IngestFinancialDataItemAsync` deletes the game's rows for that date (`ExecuteDeleteAsync`,
inside the batch transaction) and inserts the new set. Restated dates update; a date sent
twice is idempotent (staged rows of the first copy are detached). The manual upload uses
the same replace. Until 2026-10 a unique index on those five columns sent every batch to
the DLQ; the collector's highwatermark had advanced anyway, so recovering needs a resync
from the collector side (`run_daily_financial --since`).

Each item's `reference_data` also refreshes `steam_country` (code → name, Steam region) and
`steamworks_bundle` (id → name); sales by region join through `steam_country`. Read side:
`GET /api/games/{appId}/sales/daily|countries` and `GET /api/sales/summary`. "Net" is Steam's
`net_sales_usd` (after refunds and VAT, before Steam's share). **Steam sends returns as
negatives** (`gross_units_returned` −17, `net_units_sold` = sold + returned); ingestion
stores `ReturnsUnits`/`GrossReturnsUsd` positive so the `NetUnits` column
(`SalesUnits - ReturnsUnits`) is right. Key activations (`gross_units_activated`, about a
third of all lines, no revenue) go to `ActivatedUnits`, never `SalesUnits`. Decimal `Sum` works on the
SQLite test database (EF's `ef_sum`), so the tests cover the real queries.

### Steamworks store traffic (`steam_traffic_breakdown`)

The `/apps/navtrafficstats/{appid}` CSV (partner.steamgames.com) has no date column: each
row is a page category/feature total for the requested range. So rows are keyed by period,
`SnapshotDate`..`PeriodEnd` (GMT, inclusive): the collector stores one-day periods daily and
backfills history as whole months, never overlapping. **Totals are not a row**: measured,
the non-`Country` rows sum to the page's impressions/visits; `Bot Traffic` carries visits
only; the `Country` rows (feature = country code) split the same totals again. The read side
(`/api/games/{appId}/store-traffic/periods|breakdown`) applies exactly that, and a range keeps
only periods that lie entirely inside it. Batch source `steamworks_store_traffic`; the manual
upload (`POST /api/ingestion/store-traffic`) shares the upsert and files rows under the
upload day.

### Steamworks Nav Stats (`steamworks_nav_stat`)

The publisher page `/pub/navstats/{publisher}` (partner.steamgames.com) gives one CSV row
per product (demos and playtests too) for a range and a filter set. Rows are keyed by
publisher, period, `Attribution` (`all`, then its parts `store`, `steamworks`, `external`),
`Region` (only `All Regions` so far) and **app id, not game**: CATO has no game row for most
demos, so nothing is stubbed. Periods follow the store traffic rules (daily rows, monthly
history, never overlapping). A product's `all` visits equal its store traffic visits plus
bot visits; the rows add up to slightly less than the page headline, which counts products
the CSV leaves out. Batch source `steamworks_nav_stats`; the envelope's `app_id` carries the
publisher id. Rows missing from a later export are kept. Read side: `GET /api/nav-stats`.

### Steamworks players and app details (`steamworks_players`)

One item per owned game from partner.steampowered.com HTML pages (no CSV). `days` is the
whole daily peak concurrent / daily active users history from the players page; it goes
into `active_users_history` (key `GameId, RecordedAt` = the day at 00:00 UTC, new column
`PeakConcurrentUsers`; `Mau` stays 0, rows of the manual upload keep their values).
`countries` is yesterday's "Players by region" table, shares not counts, stored in
`steamworks_player_country` and replaced per date; Steam shows only yesterday, so there is
no history before the job. `details` / `demo_details` are daily snapshots of the app
details page (median time played, lifetime unique users) in `steamworks_app_details`,
keyed by `AppId, SnapshotDate`; a demo row carries the main game's `GameId`. A part sent
as null leaves its table alone. Read side: `GET /api/games/{appId}/players`.

### Steamworks demo downloads (`demo_download`)

The demo's `nav_regions.php?csv=1&downloads=1` CSV (partner.steampowered.com) has no date
column: region and country totals for the requested range. Rows are keyed by period,
`SnapshotDate`..`PeriodEnd`, like store traffic (daily rows, monthly history, never
overlapping), and filed under the **main game** with `DemoAppId`. Batch source
`steamworks_demo_downloads`; the collector finds the demos in `steamworks_app_details`.
The manual upload (`POST /api/ingestion/demo-downloads`) files its rows under the upload
day (`PeriodEnd = SnapshotDate`). Read side: `GET /api/games/{appId}/demo-downloads`.

### Steamworks bundle sales (`steamworks_bundle_sales_daily`, `steamworks_bundle_package_sales_daily`)

Two partner-site exports per bundle (`QueryBundleSalesForCSV`, `QueryBundlePackageSalesForCSV`)
with a `DateLocal` column: day rows keyed by (`BundleId`, `PartnerId`, `DateLocal`) and
(`BundleId`, `PackageId`, `DateLocal`), money `decimal(18,4)`. Steam sends returns negative;
they are stored positive, as in `steam_sale_financial`. Batch source `steamworks_bundle_sales`
(one item per bundle and window; the envelope's `app_id` is the bundle id); the bundle name
goes to `steamworks_bundle`. A day missing from a later export keeps its row. Read side:
`GET /api/bundles?from&to` and `GET /api/bundles/{bundleId}`; a package's game comes from
its lines in `steam_sale_financial`.

### Achievement percentages and game news (backend watchers, keyless Web API)

`GameAchievementSchemaWatcherService` (every 6 h, Sourcing + Owned) also stores, for **Owned**
games only, the day's global unlock percentages (`GetGlobalAchievementPercentagesForApp/v2`, no
key; a 403/404 means the app has no stats) in `game_achievement_percent`, keyed
(`GameId`, `ApiName`, `SnapshotDate`) and updated in place within the day. Sourcing games are left
out to keep the daily table small. `GameNewsWatcherService` (`News:` settings, every 6 h, Owned
games) upserts the newest `News:Count` items of `GetNewsForApp/v2` into `game_news` by gid with
the manual upload's mapping (`Source = "news"`). Both writes live in `SteamPublicStatsStore`.
Read side: `GET /api/games/{appId}/achievements` (schema joined with the latest percentages) and
`GET /api/games/{appId}/news` (newest first, plain-text excerpt). Spreadsheet rows #20 and #21.

### Steam Next Fest recaps (`steamworks_nextfest_stat`)

The publisher's `/recap/nextfest/<slug>/<publisher>` pages (partner.steamgames.com) embed
`demo_stats`: per game, cumulative snapshots during the fest (demo players, wishlists,
wishlists from demo players), about one per day. Batch source `steamworks_nextfest`, one item
per fest and game (`app_id` = the game); rows keyed (`AppId`, `FestKey`, `SnapshotEnd`), so
re-reading a past fest changes nothing. `FestKey` is Steam's sale page id
(`sale_nextfest_june_2025`). Read side: `GET /api/next-fest` (fests newest first, each game's
last snapshot and its daily rows). Spreadsheet row #11.

### Steamworks wishlist reports (owned games)

`steamworks_wishlist_daily` (unique `GameId, DateLocal`) and `steamworks_wishlist_cohort`
(unique `GameId, DateLocal, MonthCohort`) hold the two partner-site CSV exports. The
collector's `run_steamworks_wishlist` re-sends each game's **whole history** every day
under batch source `steamworks_wishlist_report`; `IngestSteamworksWishlistReportItemAsync`
loads a game's rows once, untracked, and writes only new or changed ones, so a re-export
updates in place. Rows missing from a later export are kept. Both exports are **sparse**
(no row = zero), and `DateLocal` is a Pacific-time date. The manual upload
(`POST /api/ingestion/wishlists` → `IngestWishlistDataAsync`) shares the same upsert; it no
longer writes `steam_traffic`.

`steamworks_wishlist_country` (unique `GameId, DateLocal, Country`) is the third export,
wishlist actions by country, in the same item as `countries`. It is **not** full history:
the collector sends only the last ~35 days (one-off backfills send everything), so its
upsert loads only the payload's date range. `Country` is Steam's country *name* (the CSV
column is misleadingly called `CountryCode`) and `Region` is Steam's region for it; region
totals are a GROUP BY, no lookup table.

`POST /api/games/owned-sync` runs before the collector downloads anything: it creates or
promotes every app the collector's Steamworks login can see to `GameType = "Owned"` (Redis after save) and
queues Steam enrichment in the background for rows with `LastEnrichedAt == null`. It is
synchronous so the wishlist batch never stubs a new game as `Other`.

`GroupMemberCountSnapshot` holds **two series in one table**, discriminated by `Source` and keyed `(GameId, SnapshotDate, Source)`:
`steam_community_group` (the live daily scrape) and `steamdb_follower_history` (the SteamDB backfill of the same metric, reaching years further back). Any query or upsert against this table must filter on `Source`, or the daily scrape will silently overwrite backfilled history.

### Follower history queue

The collector's `run_steamdb_followers` fetches a game's whole SteamDB follower graph
from `steam:appids:steamdb_follower_history`, lowest score first. The backend owns the
bands (`FollowerHistoryQueue`); the collector mirrors `RequestBandMax`:

| Score | Meaning | Written by |
|---|---|---|
| < -1e9 (`-epoch`) | requested, newest first | `POST /api/games/{appId}/follower-history/fetch`, old `game.analyzed` path |
| -2e6 | Owned game: refreshed after 20 h when it is in no SteamDB list, else after 30 days | `FollowerHistorySweepService` |
| -1e6 + rank | SteamDB list game, best rank first, refreshed after 30 days | `FollowerHistorySweepService` |
| 0 | seeded Sourcing/Owned, never fetched | `RedisAppIdSyncService` |
| > 0 | last successful fetch | collector `mark_success` |

The collector fetches every score <= 0 and fetches a negative score fresh from SteamDB;
it never revisits the positive band on its own. The hourly sweep reads Owned games and
every game with a `steamdb_snapshot` row in the last 7 days (the list scrape misses
days), skips requested and quarantined games, and writes only changed scores. A request
always writes, and clears quarantine and the failure count: someone asked for that game.
Each sweep also writes the backfill state as counters of its own `job_run` row
(`owned_done`, `owned_waiting`, `listed_done`, `listed_waiting`, `requests_waiting`);
the Jobs page reads the newest one.
The endpoints are open like the rest of the API.

A list value (`steamdb_snapshot.Follows`) is the graph's number 0-1 days later
(measured: 99.7% of 5,235 pairs), so a list game needs one graph fetch and the daily
list carries it after that. `GET /api/games/{appId}/followers` merges the series: graph
rows up to the graph's last day, then list values, then the group scrape. List values
are never written into `group_member_count_snapshot`: the 0-1 day offset would put
them on the wrong date.

### Job schedules (`scheduled_job`)

One row per job. A collector job has a cron, a time limit and an agent group; the job
dispatcher (worker) turns its slots into `job_dispatch` rows and the collector's
`cato-agent@<group>` claims them (`ManagedBy` `dispatcher`; `cron` while the host
crontab still starts it). A backend watcher has `ManagedBy` `worker` and an
`IntervalMinutes` instead: before every cycle it calls
`WatcherSchedule.WaitForNextRunAsync`, which reads its row once a minute and returns when
the row is enabled and the interval has passed since the last cycle ended. The first read
writes the row from the watcher's settings (`IntervalMinutes`, `Enabled`); after that the
settings are only the fallback when the database cannot be read. So
`PUT /api/scheduled-jobs/{name}` changes a watcher's interval or pauses it within a
minute, without a deploy. A missing Steam Web API key still stops the watchers that need
one, whatever the row says.

### Alerts (`alert_incident`)

`AlertMonitorService` (worker role only) runs `AlertService.RunCycleAsync` every minute,
two minutes after start so a deploy does not alert. Each rule judges the **current**
state and returns firing / not firing; firing opens one incident per job and rule
(`OpenKey` = `job|rule`, unique, null once resolved), not firing resolves it.

| Rule | Fires when | Resolves when |
|---|---|---|
| `run_failed` | the newest finished run is `Failed`, or `Lost` (for watchers only a hang: no finish within the limit) | the newest finished run succeeded |
| `items_failed` | `items_failed / items_total` >= the job's `AlertFailedRatio` and >= `AlertMinItems` | a newer run is below that |
| `missed_run` | a dispatch is pending > 5 min with its agent group idle, or expired | the newest dispatch is claimed or finished |
| `agent_offline` | an agent seen this week is silent > 10 min | it calls again |
| `dlq_not_empty` | `cato-ingestion-dlq` has messages | it is empty |
| `queue_stuck` | a queue has messages and the count has not dropped for 30 min | the count drops |
| `stale_data` | the newest data of an enabled job is older than its limit | newer data arrives |

`stale_data` reads `JobFreshnessChecks` (`Services/JobRuns/JobFreshness.cs`): per job the
newest value of a date column in the table it writes (`ccu_history.Timestamp` within
4.5 h, daily tables within 2-3 days of their date's midnight, ...). It finds a job that
reports `Succeeded` but writes nothing. The checks are typed queries in code, on indexed
columns or small tables, cached 10 minutes; `GET /api/job-runs/freshness` shows them.
A new collector job needs an entry there.

`Skipped` and `Interrupted` runs decide nothing. A time-limit stop is not left as
`Interrupted`: the job sees only a SIGTERM, but the agent's finish (exit 124/137, note
`time limit reached`) makes the run `Failed` (`JobDispatchService.FinishAsync`), so it
alerts. Until 2026-10-21 `steamworks_bundles` hit its limit daily and nobody was told.
A watcher run lost to a worker restart
(or replaced by the next cycle after one) does not alert; the heartbeat alarm reports a
dead worker.

**Lost and the worker's start.** A backend run still `Running` that started before the
**worker** last started is `Lost` ("the worker restarted during this run"). The worker
(role all or worker) writes its start to `worker_state` (`worker_started_at`,
`WorkerStartRecorder`); a role-api process reads it there (`WorkerStartTime`), so an
API-only restart no longer marks the worker's runs lost. With no recorded start, no run is
lost by a restart. A watcher cycle that fails while the app stops is stored as
`Interrupted` (`JobRunTracker`).
Messages go to every `IAlertNotifier` (SNS e-mail, Discord); `OpenSentTo` and
`ResolveSentTo` record the channels that sent, so a failed channel is retried alone
next cycle and a "resolved" message goes only where the "opened" one went. Queue counts
come from a passive declare (plain AMQP, works on Amazon MQ). The monitor also sends
`CATO/WorkerHeartbeat` to CloudWatch; the alarm `cato-worker-heartbeat`
(`infra/phase1`) e-mails after five silent minutes. Everything is off unless
`ALERTS_ENABLED=true` in `.env`; `POST /api/alerts/test` sends one message to every
channel and `GET /api/alerts` lists incidents. A collector run reports
`CATO_DISPATCH_ID` as `job_run.DispatchId`.

### Write protection and the Steamworks login form

Every non-GET request through Caddy (`https://catoptricgames.duckdns.org/api/...`) needs
the admin password, user `cato` (Caddy `basic_auth`, hash `CATO_ADMIN_HASH` in `.env`,
set with `scripts/set-admin-password.sh`; until then a hash of a thrown-away string keeps
writes locked). Reads stay open. The API port 5039 is bound to `127.0.0.1` only: the
collector's jobs and agents call `localhost:5039` directly, OGT calls `api:8080` on the
Docker network, and people go through Caddy. Remember that the frontend's paths are
`/api/api/...` (Caddy strips the first `/api`).

`GET /api/credentials/steamworks` shows the Steamworks login of the collector's jobs from
the secret store: the masked account, the refresh token's expiry and days left, and the
last session token (its issue time is the last refresh). `PUT
/api/credentials/steamworks/refresh-cookie` takes the `steamRefresh_partner` value or the
whole cookie header, checks it like the collector's `--set-refresh-cookie` (a Steam JWT,
lifetime over a week, not expired, a warning for a second account in the paste) and
writes `cato/steamworks/refresh`. No endpoint returns a cookie, and the log has only the
masked account.

**AWS foundation for the ECS split:** `infra/phase2/` (Terraform, not applied yet; `README.md` has
the apply order, the cost and the Phase 3 prerequisites; `deploy.sh` builds, pushes, migrates and
rolls the services).

## Configuration

Settings are in `src/Cato.API/appsettings.json`. Key sections: `ConnectionStrings:DefaultConnection`, `RabbitMQ`, `SteamKit`, `Ingestion:DataPath`.

**Secrets are not in appsettings.** With `Secrets:Backend = aws` (`SECRETS_BACKEND=aws` in the
server's `.env`) the API reads the Secrets Manager secret `cato/backend/config` at start: a
JSON object of configuration keys (`ConnectionStrings:DefaultConnection`,
`SteamKit:Username`, `SteamKit:Password`, `SteamWebApi:ApiKey`), added last so it wins over
appsettings and the environment; the log names the keys, never the values. The SteamKit
refresh token is the rotating secret `cato/steam/steamkit-auth`. For local development set
the Steam account with `dotnet user-secrets` or `SteamKit__Username` / `SteamKit__Password`.
The Steam password was in git history until 2026-10-21; rotate it.

The other containers' secrets (Caddy's DuckDNS token, Seq's first-run password, OGT's
database login and invite code) are the secret `cato/server/compose-env`, a JSON object
`{service: {NAME: value}}`. `cato-compose-secrets.service` (`infra/phase1/systemd`) runs
`scripts/compose-secrets.py` at boot, which writes `/run/cato/<service>.env` (tmpfs, mode
600); compose reads them with `env_file`. After a rotation: `sudo systemctl restart
cato-compose-secrets`, then recreate that service. The backend `.env` holds no secret.

The container runs as `Production` (Dockerfile and compose); `appsettings.Development.json`
holds only local-development values. Other settings:

| Key | Default | Server (compose) |
|---|---|---|
| `Cors:AllowedOrigins` | none (Development: `localhost:5173`, `localhost:3000`) | the site origin; the frontend calls its own origin anyway |
| `Seq:ServerUrl` | none, so no Seq sink (Development: `localhost:5341`) | `http://seq:80` |
| `Swagger:Enabled` | false (Development: true) | true |
| `Database:MaxPoolSize` | per role: all 60, api 30, worker 40, migrate 5 | the role default |

Scope and start-up DI validation stay on in Production (`UseDefaultServiceProvider`). The
start log has one `Configuration:` line with the environment, pool limit, Seq, Swagger and
CORS origins.

# Agent Guidance: dotnet-skills

IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
Workflow: skim repo patterns -> consult dotnet-skills by name -> implement smallest-change -> note conflicts.

Routing (invoke by name)
- C# / code quality: modern-csharp-coding-standards, csharp-concurrency-patterns, api-design, type-design-performance
- ASP.NET Core / Web (incl. Aspire): aspire-service-defaults, aspire-integration-testing, transactional-emails
- Data: efcore-patterns, database-performance
- DI / config: dependency-injection-patterns, microsoft-extensions-configuration
- Testing: testcontainers-integration-tests, playwright-blazor-testing, snapshot-testing

Quality gates (use when applicable)
- dotnet-slopwatch: after substantial new/refactor/LLM-authored code
- crap-analysis: after tests added/changed in complex code

Specialist agents
- dotnet-concurrency-specialist, dotnet-performance-analyst, dotnet-benchmark-designer, akka-net-specialist, docfx-specialist
