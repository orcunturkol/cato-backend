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

  **All three consumers share one channel, and RabbitMQ.Client dispatches its
  callbacks with a concurrency of 1** (`ConsumerDispatchConcurrency` is never set, so
  it takes the client default). One slow handler therefore blocks *every* queue, not
  just its own. This is not theoretical: an inline Steam call in `IngestionService`
  froze `cato-game-analyzed` at 1829 messages and 0/min while `cato-ingestion-v2`
  crawled at one app per 15s, and every queue showed exactly 1 unacked message. The
  symptom looks nothing like the cause — the API is healthy, the logs are busy, and
  the stalled queue is silent. Keep every handler fast; anything slow belongs in a
  background service.

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

`GroupMemberCountSnapshot` holds **two series in one table**, discriminated by `Source` and keyed `(GameId, SnapshotDate, Source)`:
`steam_community_group` (the live daily scrape) and `steamdb_follower_history` (the SteamDB backfill of the same metric, reaching years further back). Any query or upsert against this table must filter on `Source`, or the daily scrape will silently overwrite backfilled history.

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
