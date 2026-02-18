# CATO Backend Development Roadmap v2
## Solo Developer - Kanban Approach

**Last Updated:** February 6, 2026

**Context:** This roadmap is based on the existing Python data collectors (`catoptric-data-collector`) and aims to build a .NET backend that consumes, stores, and analyzes the collected data while also implementing the full MVP data structure from the CATO Data Yapısı specification.

---

## Overview

### Architecture Strategy
- **Backend:** ASP.NET Core Web API (.NET 8/9) with PostgreSQL
- **Data Collection:** Keep existing Python collectors as standalone services; integrate outputs via file ingestion or REST endpoints
- **Architecture Pattern:** Vertical Slice Architecture for maintainability
- **Deployment:** .NET backend as primary API; Python collectors as scheduled jobs/microservices

### Key Principles
1. **Leverage existing work:** Don't rewrite Python collectors - integrate them
2. **Incremental delivery:** Each phase delivers working functionality
3. **Solo dev friendly:** Limit WIP, focus on finishing one thing at a time
4. **Data-first:** Database schema drives feature development

---

## Phase 0: Assessment & Setup (CURRENT PHASE)
**Duration:** 1-2 days
**Goal:** Understand what exists, establish development environment

### [✓ Completed]
- [x] Review Python data collectors functionality
- [x] Review collected data samples
- [x] Review PDF specification for MVP requirements
- [x] Create this roadmap

### [To Do]
- [ ] **Database Schema Design**: Design complete PostgreSQL schema based on PDF specification (all 13+ tables)
- [ ] **Initialize .NET Project**: Create ASP.NET Core Web API project
  ```bash
  dotnet new webapi -n Cato.API
  dotnet new classlib -n Cato.Domain
  dotnet new classlib -n Cato.Infrastructure
  ```
- [ ] **Install Core Packages**:
  - `Npgsql.EntityFrameworkCore.PostgreSQL`
  - `MediatR`
  - `Carter` (or FastEndpoints)
  - `FluentValidation`
  - `Hangfire` (for scheduled jobs)
- [ ] **Setup Development Environment**:
  - PostgreSQL database instance
  - Connection strings in `appsettings.json`
  - User Secrets for sensitive keys (`dotnet user-secrets init`)
- [ ] **Create Initial Migration**: Generate first EF Core migration with all tables

---

## Phase 1: Foundation & Core Tables
**Duration:** 3-5 days
**Goal:** Have a running API with core game tables and basic CRUD operations

### Database Tables (Priority 1)
- [ ] **MAIN_GAME**: Core game catalog (owned, competitor, sourcing games)
  - Fields: AppID, Name, Type (Owned/Competitor/Sourcing), Tags, Genres, Release Date, Price, Developer, Publisher, Early Access Status
  - Relationships: Links to GAME_GENRE, GENRE_TAG
- [ ] **GAME_GENRE**: Genre taxonomy
  - Fields: Genre ID, Name, Type (Primary/Secondary), Source (Steam/Internal)
- [ ] **GENRE_TAG**: Detailed tag system
  - Fields: Game ID, Tag ID, Name, Type (Genre/Subgenre/Mechanic/Theme), Weight/Score
- [ ] **LEGAL_ENTITY**: Publishers & Developers
  - Fields: Entity ID, Name, Type (Publisher/Developer), Contact Info

### Features to Implement
- [ ] **Entity Models**: Create C# domain entities for above tables
- [ ] **DbContext Setup**: Create `CatoDbContext` with all entity configurations
- [ ] **Migrations**: Apply initial database migration
- [ ] **CRUD - Games**: 
  - `Features/Games/CreateGame.cs` (Command + Handler)
  - `Features/Games/GetGameDetails.cs` (Query + Handler)
  - `Features/Games/ListGames.cs` (Query + Handler with filtering)
  - `Features/Games/UpdateGame.cs` (Command + Handler)
- [ ] **Steam API Enrichment**: Create `Features/Games/EnrichGameFromSteam.cs`
  - Takes AppID, fetches from Steam Store API, populates game fields
  - Implement the **Story 1.3** requirement from PDF

### API Endpoints (Carter/FastEndpoints)
```
POST   /api/games                    # Create game (manual or with AppID only)
GET    /api/games/{id}                # Get game details
GET    /api/games                     # List games (with filters)
PATCH  /api/games/{id}                # Update game
POST   /api/games/{id}/enrich         # Fetch data from Steam and populate
```

---

## Phase 2: Data Ingestion - Python Collector Integration
**Duration:** 5-7 days
**Goal:** Import existing Python collector outputs into .NET backend database

### Integration Strategy
**Option A (Recommended for MVP):** File-based ingestion
- Python collectors write JSON files to shared volume/directory
- .NET backend has ingestion endpoints/jobs that read JSON and insert to DB

**Option B:** REST API integration
- .NET exposes ingestion endpoints
- Python collectors POST data directly to .NET API

### Tables for Ingestion
- [ ] **STEAM_SALE_FINANCIAL**: Financial data from Steam Partner API
  - Source: `steam_financial_data_collector.py` → `GetDetailedSales`
  - Fields: Game ID, Date, Country, Sales Units, Gross Revenue, Steam Commission, Tax, Net Revenue, Currency, Discount ID
- [ ] **STEAM_TRAFFIC**: Store page traffic metrics
  - Source: Steamworks Reporting (CSV exports mentioned in PDF)
  - Fields: Game ID, Date, Visits, Impressions, CTR, Wishlist Adds, Purchase Conversion, Traffic Source
- [ ] **OWNED_GAMES**: Extended data for published games
  - Source: `steamworks_data_collector.py` → Wishlist CSVs + Financials
  - Inherits/extends MAIN_GAME data

### Features to Implement
- [ ] **Ingest Financial Data**: `Features/Ingestion/IngestFinancialData.cs`
  - Reads Python collector JSON output (from `steam_financial_data_collector.py`)
  - Maps to `STEAM_SALE_FINANCIAL` table
  - Implements idempotency (no duplicates on re-run)
- [ ] **Ingest Wishlist Data**: `Features/Ingestion/IngestWishlistData.cs`
  - Reads `steamworks_wishlists.csv` output
  - Stores in appropriate tables
- [ ] **Ingest SteamDB Data**: `Features/Ingestion/IngestSteamDbData.cs`
  - Reads `steamdb_data_collector.py` outputs (wishlist rankings, CCU)
  - Enriches MAIN_GAME or creates tracking tables
- [ ] **Ingest Gamalytic Data**: `Features/Ingestion/IngestGamalyticData.cs`
  - Reads `gamalytic_data_collector.py` outputs
  - Maps to relevant tables (sales, CCU history, etc.)
- [ ] **Background Job - Daily Ingestion**: Setup Hangfire job
  - Runs daily at 04:00 UTC (**Story 1.2** requirement)
  - Calls all ingestion features sequentially
  - Logs success/failure to file or webhook

### Python Collector Improvements (Optional)
- [ ] Add `--output-json` flag to collectors for standardized JSON output
- [ ] Create wrapper script that runs all collectors and outputs to `/shared/data`
- [ ] Add timestamp metadata to all JSON outputs

---

## Phase 3: Competitor Tracking & Scraping
**Duration:** 3-4 days
**Goal:** Track competitor games with automated CCU, wishlist, and update monitoring

### Tables
- [ ] **COMPETITOR_TRACKING**: Metadata for tracking
  - Fields: Game ID (FK to MAIN_GAME), Tracking Enabled, Added Date, Notes
- [ ] **CCU_HISTORY**: Concurrent user tracking
  - Fields: Game ID, Timestamp, CCU Count, Source (Steam API / SteamDB)
- [ ] **WISHLIST_RANK_HISTORY**: SteamDB wishlist rank over time
  - Fields: Game ID, Date, Rank, Wishlists Count (if available)

### Features
- [ ] **Add Competitor**: `Features/Competitors/AddCompetitorGame.cs`
  - Adds game to MAIN_GAME with type="Competitor"
  - Enriches from Steam
  - Enables tracking
  - Implements **Story 2.2** requirement
- [ ] **CCU Tracking Job**: `Features/Competitors/TrackCCUJob.cs`
  - Runs every 15 minutes (**Story 1.1** requirement)
  - Queries Steam API `GetNumberOfCurrentPlayers` for all tracked games
  - Stores in CCU_HISTORY table
- [ ] **Wishlist Rank Job**: `Features/Competitors/TrackWishlistRankJob.cs`
  - Runs daily (can piggyback on Python SteamDB collector)
  - Reads SteamDB scraper output
  - Updates WISHLIST_RANK_HISTORY
- [ ] **Follower Count Scraper**: Integrate `group_member_count_scraper.py` output
  - Add followers count to MAIN_GAME table
  - Track history if needed

### Background Jobs
```csharp
// Hangfire job registration
RecurringJob.AddOrUpdate<TrackCCUJob>("track-ccu", x => x.Execute(), "*/15 * * * *");
RecurringJob.AddOrUpdate<TrackWishlistRankJob>("track-wishlist", x => x.Execute(), "0 4 * * *");
```

---

## Phase 4: Marketing Actions & Targets
**Duration:** 5-7 days
**Goal:** Model and track marketing operations (influencers, mailings, events, discounts)

### Tables
- [ ] **ACTION**: Marketing actions catalog
  - Fields: Action ID, Type (Mailing/Influencer/Event/Discount/Bundle), Decision Source (Manual/Rule/AI), Status (Planned/Outreach/Executed/Completed/Cancelled), Created Date, Description
- [ ] **GAME_ACTION**: Many-to-many link between games and actions
  - Fields: Game Action ID, Action ID, Game ID, Role (Primary/Secondary for bundles)
- [ ] **MARKETING_TARGET**: Target entities (Influencers/Media/Events)
  - Fields: Target ID, Name, Type (Influencer/Media/Event), Contact Info, Preferred Genres/Tags, Audience Size, Notes
- [ ] **ACTION_TARGET**: Links actions to targets
  - Fields: Action Target ID, Action ID, Target Type, Target ID, Outreach Date, Status (Contacted/Accepted/Rejected/Live)
- [ ] **TARGET_MATCH**: Pre-calculated match scores between games and targets
  - Fields: Game ID, Target ID, Genre/Tag Reference, Lifecycle Stage (Pre-launch/Launch/Live), Relevance Score, Sample Size

### Features
- [ ] **CRUD - Marketing Targets**: 
  - Create/List/Update Influencers, Events, Mailing Lists
  - `Features/Marketing/CreateTarget.cs`
  - `Features/Marketing/ListTargets.cs`
- [ ] **CRUD - Actions**:
  - `Features/Marketing/CreateAction.cs` (Implements **Story 2.1**)
  - `Features/Marketing/ListActions.cs`
  - `Features/Marketing/UpdateActionStatus.cs`
- [ ] **Link Action to Game**: `Features/Marketing/LinkGameToAction.cs`
  - Creates GAME_ACTION record
  - Dropdown/autocomplete in UI
- [ ] **Link Action to Target**: `Features/Marketing/LinkTargetToAction.cs`
  - Creates ACTION_TARGET record
- [ ] **Calculate Target Match**: `Features/Marketing/CalculateTargetMatch.cs`
  - Algorithm: Match game genres/tags with target preferences
  - Scoring logic (e.g., FPS game + FPS-loving influencer = high score)
  - Stores in TARGET_MATCH table
  - Auto-triggered when new game or target added

---

## Phase 5: Impact Analysis & Intelligence
**Duration:** 5-7 days
**Goal:** Measure ROI of marketing actions by correlating with sales/wishlist/traffic data

### Tables
- [ ] **ACTION_IMPACT**: Measured impact of actions
  - Fields: Action Impact ID, Action ID, Measurement Period (Start/End), Wishlist Change, Sales Change, Revenue Change, Traffic Change, Notes

### Features
- [ ] **Calculate Action Impact**: `Features/Analytics/CalculateActionImpact.cs`
  - Inputs: Action ID
  - Logic:
    1. Get action date from ACTION table
    2. Query STEAM_SALE_FINANCIAL for 7 days before (baseline)
    3. Query STEAM_SALE_FINANCIAL for 7 days after (result)
    4. Calculate % change in sales, revenue
    5. Query STEAM_TRAFFIC for traffic uplift
    6. Query OWNED_GAMES wishlist data for wishlist uplift
    7. Store results in ACTION_IMPACT table
  - Returns: ImpactReport DTO
- [ ] **Dashboard Summary Endpoint**: `Features/Analytics/GetDashboardSummary.cs`
  - Aggregates data for frontend charts
  - Returns: Sales trends, top-performing actions, recent activities
- [ ] **Game Timeline Query**: `Features/Analytics/GetGameTimeline.cs`
  - Implements **Story 3.1** requirement
  - Returns: CCU/Wishlist time series + action markers for visualization
  - Format suitable for chart libraries (e.g., Chart.js)
- [ ] **Regional Revenue Report**: `Features/Analytics/GetRegionalRevenue.cs`
  - Implements **Story 3.2** requirement
  - Queries STEAM_SALE_FINANCIAL grouped by country
  - Returns: Top 10 countries by revenue + refund rates

### API Endpoints
```
POST   /api/analytics/actions/{id}/calculate-impact
GET    /api/analytics/dashboard/summary
GET    /api/analytics/games/{id}/timeline?start=&end=
GET    /api/analytics/revenue/regional?start=&end=
```

---

## Phase 6: Automation & Polish
**Duration:** 3-5 days
**Goal:** Fully automated data pipeline with monitoring and error handling

### Features
- [ ] **Orchestration Service**: `Infrastructure/Orchestration/DataPipelineOrchestrator.cs`
  - Daily job at 04:00 UTC
  - Sequence:
    1. Trigger Python collectors (via shell exec or REST)
    2. Wait for completion
    3. Run .NET ingestion jobs
    4. Run impact calculations for recent actions
    5. Send success/failure notification
- [ ] **Error Logging & Alerting**:
  - Log failed API calls, scrapes, ingestions to structured logs
  - Discord/Slack webhook integration for critical failures
  - Hangfire dashboard for job monitoring
- [ ] **Rate Limiting & Retry Policies**:
  - Implement Polly for Steam API calls
  - Respect rate limits (configurable delays)
  - Exponential backoff for transient failures
- [ ] **Health Check Endpoint**: `GET /health`
  - Checks database connectivity
  - Checks last successful ingestion time
  - Checks Python collector status (if possible)

### Technical Debt
- [ ] Add comprehensive logging (Serilog)
- [ ] Add API documentation (Swagger/OpenAPI)
- [ ] Add unit tests for critical business logic (impact calculation)
- [ ] Add integration tests for ingestion pipelines

---

## Phase 7: Admin UI (Optional/Future)
**Duration:** 5-10 days
**Goal:** Web interface for managing games, actions, and viewing analytics

### Tech Stack Options
- **Option A:** Blazor Server (stays in .NET ecosystem)
- **Option B:** React/Next.js SPA (modern, more flexible)
- **Option C:** Razor Pages (simple, fast to build)

### Key Pages
- [ ] **Games Dashboard**: List all games, filter by type, search
- [ ] **Game Detail Page**: View stats, actions, impact over time
- [ ] **Actions Manager**: Create/edit actions, link to games/targets
- [ ] **Targets Manager**: CRUD for influencers/events
- [ ] **Analytics Dashboard**: 
  - Charts for **Story 3.1** (timeline with action markers)
  - Tables for **Story 3.2** (regional revenue)
  - KPI cards (total revenue, active actions, tracked games)
- [ ] **Ingestion Status**: View last sync times, errors, manual trigger

---

## Non-Functional Requirements (ALL PHASES)

### Security
- [ ] **Secret Management**: Never hardcode keys
  - Development: .NET User Secrets
  - Production: Azure Key Vault or environment variables
- [ ] **API Authentication**: Add JWT or API Key auth (Phase 6+)
- [ ] **CORS Policy**: Configure allowed origins for frontend

### Reliability
- [ ] **Idempotency**: All ingestion operations must be idempotent
  - Use upsert patterns (INSERT ... ON CONFLICT UPDATE)
  - Check for existing records by unique keys (Game ID + Date)
- [ ] **Rate Limiting**: Respect Steam API limits
  - Steam Store API: ~200 requests/5 minutes
  - Steam Web API: varies by key tier
  - Implement delays and backoff (Polly library)
- [ ] **Transaction Management**: Use EF Core transactions for multi-table inserts

### Monitoring
- [ ] **Structured Logging**: Serilog with JSON output
- [ ] **Job Monitoring**: Hangfire dashboard (`/hangfire`)
- [ ] **Error Notifications**: Discord/Slack webhooks for failures

---

## Success Metrics

### Phase 1-2 Success
- [ ] Can manually add a game by AppID and auto-populate data from Steam
- [ ] Python collector outputs successfully ingested to PostgreSQL
- [ ] No duplicate records on re-import

### Phase 3 Success
- [ ] CCU tracking running every 15 minutes for all competitor games
- [ ] Can add a competitor game with one click and it starts tracking automatically

### Phase 4 Success
- [ ] Can create a marketing action linked to games and targets via UI/API
- [ ] Target match scores calculated automatically

### Phase 5 Success (MVP COMPLETE)
- [ ] Impact calculation accurately measures before/after metrics
- [ ] Dashboard shows actionable insights:
  - "Influencer X resulted in +500 wishlists"
  - "Email campaign ROI: +$10K revenue"
- [ ] Can answer: **"Did this marketing action work?"**

---

## Timeline Estimate (Solo Developer)

| Phase | Duration | Cumulative |
|-------|----------|------------|
| Phase 0: Setup | 1-2 days | 2 days |
| Phase 1: Foundation | 3-5 days | 7 days |
| Phase 2: Ingestion | 5-7 days | 14 days |
| Phase 3: Competitor Tracking | 3-4 days | 18 days |
| Phase 4: Marketing Actions | 5-7 days | 25 days |
| Phase 5: Impact Analysis (MVP) | 5-7 days | **32 days** |
| Phase 6: Automation & Polish | 3-5 days | 37 days |
| Phase 7: Admin UI (Optional) | 5-10 days | 47 days |

**MVP Delivery Target:** ~5-6 weeks (32 working days)
**Full System with UI:** ~8-9 weeks (47 working days)

---

## Next Immediate Actions

1. **Design database schema** in SQL or EF Core model classes
2. **Create .NET solution structure** with projects
3. **Setup PostgreSQL** and connection strings
4. **Implement MAIN_GAME table and CRUD operations**
5. **Test enrichment** by adding a game via AppID only

---

## Notes & Decisions

### Why Keep Python Collectors?
- Already working and battle-tested
- Handle complex scraping (Selenium, Cloudflare bypass)
- Rewriting in C# would take weeks with little benefit
- Integration via files/REST is pragmatic for MVP

### Why PostgreSQL?
- Excellent JSON support (for flexible data storage)
- Great performance for analytical queries
- EF Core has mature Npgsql provider
- Free and open-source

### Why Vertical Slice Architecture?
- Solo dev: minimize context switching
- Feature-based folders = easy navigation
- No over-abstraction (IRepository, IService bloat)
- Faster iteration during MVP phase

### Future Enhancements (Post-MVP)
- AI-powered action recommendations (analyze historical TARGET_MATCH + ACTION_IMPACT)
- Predictive wishlist modeling
- Automated reporting (weekly email with insights)
- Public API for external integrations
- Mobile app for on-the-go monitoring
