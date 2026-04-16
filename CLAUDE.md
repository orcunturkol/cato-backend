# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CATO is a Steam game analytics backend that ingests and serves financial, traffic, CCU, ownership, and ranking data for Steam games. It integrates with the Steam API and SteamKit2 for real-time data, and uses RabbitMQ for async ingestion pipelines.

## Build & Run Commands

```bash
# Build
dotnet build

# Run the API (requires PostgreSQL + RabbitMQ from docker-compose)
docker compose up -d postgres rabbitmq
dotnet run --project src/Cato.API

# EF Core migrations (run from repo root)
dotnet ef migrations add <MigrationName> --project src/Cato.Infrastructure --startup-project src/Cato.API
dotnet ef database update --project src/Cato.Infrastructure --startup-project src/Cato.API
```

The database auto-migrates on startup (`Program.cs` calls `db.Database.Migrate()`).

No test projects exist yet.

## Architecture

**.NET 10** solution with three projects:

- **Cato.API** — ASP.NET Core Web API. Controllers, MediatR handlers, DTOs, and application services. The entry point and DI composition root.
- **Cato.Domain** — Pure entity classes (no dependencies). All entities live in `Entities/`.
- **Cato.Infrastructure** — EF Core DbContext, Steam API/SteamKit2 clients, RabbitMQ messaging. References Domain only.

### Request Handling Pattern

Uses **MediatR** with command/query objects in `Models/{Feature}/` and handlers in `Services/Handlers/{Feature}/`. Feature groups: `Games`, `Ingestion`, `SteamDb`. FluentValidation is registered for request validation.

Some older services (`GameService`, `GameDataService`, `IngestionService`, `SteamKitDataService`) still exist as direct service classes in `Services/` — the project is migrating toward MediatR handlers.

### Key Infrastructure

- **Database**: PostgreSQL via EF Core (Npgsql). Connection on port **5434** (mapped from container 5432). All entity table mappings and indexes are configured via Fluent API in `CatoDbContext.OnModelCreating`. Timestamps (CreatedAt/UpdatedAt) are set automatically in `SaveChanges`/`SaveChangesAsync`.
- **Messaging**: RabbitMQ with `IngestionDispatcher` (producer) and `RabbitMqConsumerService` (hosted service consumer). Queue: `cato-ingestion`, Exchange: `cato-data`.
- **Steam Integration**: `SteamApiService` (HTTP client for Steam Web API) and `SteamKitService` (SteamKit2 for PICS change monitoring via `SteamPicsWatcherService` background service).
  - **SteamKit Game Discovery**: `SteamPicsWatcherService` polls Steam's PICS change feed via `PICSGetChangesSince`, which returns all AppIDs with any metadata change since the last known change number (persisted in `pics_change_number.txt`). For each changed AppID, it calls `PICSGetProductInfo` and reads the `common` KeyValue section. It filters by `common["type"] == "game"` (excludes DLCs, tools, demos) and `common["releasestate"] == "released"` (excludes unreleased). Also extracts `common["name"]` and `common["steam_release_date"]` (unix timestamp). Matching games are saved with `GameType = "Sourcing"`.
- **Logging**: Serilog with console sink.
- **Swagger**: Available at `/swagger` in Development.

### Database Entities

Core entity is `Game` (table: `main_game`, keyed by `AppId`). Related time-series/snapshot entities: `SteamSaleFinancial`, `SteamTraffic`, `CcuHistory`, `OwnedGameData`, `GroupMemberCountSnapshot`, `SteamDbSnapshot`, `IngestionLog`. Games have `GameGenre` and `GenreTag` collections. `LegalEntity` represents developers/publishers.

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
