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

The database auto-migrates on startup (`Program.cs` calls `db.Database.Migrate()`).

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

- **Database**: PostgreSQL via EF Core (Npgsql), hosted on AWS RDS. Terraform for the RDS instance lives in `infra/rds/`; see `infra/rds/CUTOVER_RUNBOOK.md` for provisioning, data migration, rollback, and cleanup procedures. The connection string (`ConnectionStrings__DefaultConnection` in docker-compose) is sourced from `RDS_HOST`/`RDS_DB_PASSWORD` in `.env` on the deployed host. All entity table mappings and indexes are configured via Fluent API in `CatoDbContext.OnModelCreating`. Timestamps (CreatedAt/UpdatedAt) are set automatically in `SaveChanges`/`SaveChangesAsync`.
- **Messaging**: RabbitMQ with `IngestionDispatcher` (producer) and `RabbitMqConsumerService` (hosted service consumer). Exchange: `cato-data` (topic). Three queues:
  - `cato-ingestion` — legacy single-item path, routing key `ingestion.*` → `IIngestionDispatcher`
  - `cato-ingestion-v2` — batch path, routing key `ingestion.batch.#` → `IBatchIngestionDispatcher`. New collector sources need only a case in its `source` switch; the binding is a wildcard.
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
- **Logging**: Serilog with console sink.
- **Swagger**: Available at `/swagger` in Development.

### Database Entities

Core entity is `Game` (table: `main_game`, keyed by `AppId`). Related time-series/snapshot entities: `SteamSaleFinancial`, `SteamTraffic`, `CcuHistory`, `OwnedGameData`, `GroupMemberCountSnapshot`, `SteamDbSnapshot`, `IngestionLog`. Games have `GameGenre` and `GenreTag` collections. `LegalEntity` represents developers/publishers.

### Steam store enrichment

`SteamGameEnrichmentService.EnrichGameAsync` is the single writer of a game's store
record: name, descriptions, media, price, platforms, release date, developer,
publisher, `GameGenre` rows, and `GenreTag` rows (store categories as `Mechanic`,
scraped user tags as `UserTag` with their rank as `Weight`). Two things reach it:

| Trigger | When |
|---|---|
| `SteamPicsWatcherService` | a newly discovered app, immediately after creation |
| `GameEnrichmentWatcherService` | hourly sweep — every game that has none |

The sweep (`GameEnrichment` settings section) works four bands in order:

1. reddit-analysed games with no store record (`AnalyzedAt != null`, newest first)
2. reddit-analysed games whose record has gone stale
3. everything else with no store record, by app id
4. everything else that has gone stale, stalest first

Bands 2 and 4 need `RefreshAfterDays > 0`. **Both reddit bands come before either
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

## Configuration

Settings are in `src/Cato.API/appsettings.json`. Key sections: `ConnectionStrings:DefaultConnection`, `RabbitMQ`, `SteamKit`, `Ingestion:DataPath`.

CORS allows `localhost:5173` and `localhost:3000` (frontend dev servers).

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
