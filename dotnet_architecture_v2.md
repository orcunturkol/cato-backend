# CATO .NET Backend Architecture v2
## Vertical Slice Architecture with Python Integration

**Last Updated:** February 6, 2026

---

## Table of Contents
1. [Architecture Overview](#architecture-overview)
2. [System Components](#system-components)
3. [Tech Stack](#tech-stack)
4. [Folder Structure](#folder-structure)
5. [Database Schema](#database-schema)
6. [Vertical Slice Examples](#vertical-slice-examples)
7. [Python Collector Integration](#python-collector-integration)
8. [API Design](#api-design)
9. [Background Jobs](#background-jobs)
10. [Security & Configuration](#security--configuration)
11. [Deployment Architecture](#deployment-architecture)

---

## Architecture Overview

### Why Vertical Slice Architecture?

For CATO, **Vertical Slice Architecture** is the optimal choice because:

1. **Feature-Driven Development**: Each feature (e.g., "Sync Financials", "Calculate Impact") contains all layers (API endpoint, validation, business logic, database access) in one place
2. **Solo Developer Friendly**: Minimizes file jumping and context switching
3. **Independent Features**: Marketing actions, competitor tracking, and analytics are separate domains that rarely share logic
4. **Faster Iteration**: No need to coordinate changes across Controllers, Services, Repositories
5. **Easy Refactoring**: Change one feature without affecting others

### High-Level Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                      External Systems                        │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │  Steam   │  │ SteamDB  │  │Gamalytic │  │Steamworks│   │
│  │   API    │  │ (Scrape) │  │   API    │  │   API    │   │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘   │
└───────┼─────────────┼─────────────┼─────────────┼──────────┘
        │             │             │             │
        └──────┬──────┴──────┬──────┴──────┬──────┘
               │             │             │
        ┌──────▼─────────────▼─────────────▼──────┐
        │     Python Data Collectors (Existing)    │
        │  - steam_game_data_collector.py         │
        │  - steam_financial_data_collector.py    │
        │  - steamdb_data_collector.py            │
        │  - gamalytic_data_collector.py          │
        │  Output: JSON files in /shared/data     │
        └──────────────────┬──────────────────────┘
                           │
                           │ (File ingestion or REST)
                           │
        ┌──────────────────▼──────────────────────┐
        │        .NET Core API (Cato.API)         │
        │                                          │
        │  ┌────────────────────────────────────┐ │
        │  │  Features (Vertical Slices)        │ │
        │  │  - Games/                          │ │
        │  │  - Ingestion/                      │ │
        │  │  - Marketing/                      │ │
        │  │  - Competitors/                    │ │
        │  │  - Analytics/                      │ │
        │  └────────────────────────────────────┘ │
        │                                          │
        │  ┌────────────────────────────────────┐ │
        │  │  Infrastructure                    │ │
        │  │  - Database (EF Core)              │ │
        │  │  - Background Jobs (Hangfire)      │ │
        │  │  - External APIs (Steam HTTP)      │ │
        │  └────────────────────────────────────┘ │
        └──────────────────┬──────────────────────┘
                           │
                           │
        ┌──────────────────▼──────────────────────┐
        │         PostgreSQL Database              │
        │  - 13+ tables (MAIN_GAME, ACTION, etc.) │
        └──────────────────────────────────────────┘
```

---

## System Components

### 1. Python Data Collectors (External)
**Location:** `/catoptric-data-collector/`
**Purpose:** Collect data from Steam, SteamDB, Gamalytic
**Output:** JSON files to `/shared/data/` or POST to .NET API
**Scheduling:** Cron jobs or called by .NET Hangfire jobs

### 2. .NET Core API (Cato.API)
**Location:** `/Cato.API/`
**Purpose:** Primary backend application
**Responsibilities:**
- Expose REST API for CRUD operations
- Ingest Python collector outputs
- Calculate analytics and impact
- Manage background jobs
- Serve admin UI (future)

### 3. PostgreSQL Database
**Location:** Hosted database (local dev, cloud prod)
**Purpose:** Single source of truth for all data
**Tables:** 13+ tables defined in schema section below

---

## Tech Stack

### Core Framework
- **.NET 8 or .NET 9** (LTS recommended)
- **ASP.NET Core Web API** (Minimal APIs or FastEndpoints)

### Database
- **PostgreSQL 14+** (excellent JSON support, analytical performance)
- **Entity Framework Core 8+** (ORM)
- **Npgsql.EntityFrameworkCore.PostgreSQL** (provider)

### Key Libraries

| Library | Purpose | Why? |
|---------|---------|------|
| **MediatR** | Command/Query dispatcher | Standard for Vertical Slices; decouples handlers |
| **Carter** or **FastEndpoints** | Endpoint routing | Organizes endpoints alongside handlers; cleaner than Controllers |
| **FluentValidation** | Input validation | Declarative validation rules; integrates with MediatR pipeline |
| **Hangfire** | Background job scheduling | Battle-tested; built-in dashboard; persistent queue |
| **Polly** | Resilience & retry logic | Handles transient failures for Steam API calls |
| **Serilog** | Structured logging | JSON logs; easy integration with monitoring tools |
| **Refit** (optional) | Typed HTTP client | Strongly-typed Steam API clients |

### Python Integration
- **File System Watcher** or **Manual Trigger**: .NET reads JSON outputs
- **REST Endpoints**: Python POSTs data to .NET ingestion endpoints

---

## Folder Structure

```
Cato/
├── Cato.API/
│   ├── Program.cs                          # App entry point, service registration
│   ├── appsettings.json                    # Config (connection strings, etc.)
│   ├── appsettings.Development.json
│   │
│   ├── Features/                           # 🌟 Vertical Slices (main logic here)
│   │   ├── Games/
│   │   │   ├── CreateGame.cs               # Command + Handler + Endpoint
│   │   │   ├── GetGameDetails.cs           # Query + Handler + Endpoint
│   │   │   ├── ListGames.cs                # Query + Handler + Endpoint
│   │   │   ├── UpdateGame.cs               # Command + Handler + Endpoint
│   │   │   ├── EnrichGameFromSteam.cs      # Command + Handler (calls Steam API)
│   │   │   └── GameDto.cs                  # DTOs specific to this feature
│   │   │
│   │   ├── Ingestion/
│   │   │   ├── IngestFinancialData.cs      # Command + Handler (reads Python JSON)
│   │   │   ├── IngestWishlistData.cs       # Command + Handler
│   │   │   ├── IngestSteamDbData.cs        # Command + Handler
│   │   │   └── IngestGamalyticData.cs      # Command + Handler
│   │   │
│   │   ├── Competitors/
│   │   │   ├── AddCompetitorGame.cs        # Command + Handler
│   │   │   ├── TrackCCUJob.cs              # Background job (Hangfire)
│   │   │   └── TrackWishlistRankJob.cs     # Background job
│   │   │
│   │   ├── Marketing/
│   │   │   ├── CreateAction.cs             # Command + Handler
│   │   │   ├── ListActions.cs              # Query + Handler
│   │   │   ├── LinkGameToAction.cs         # Command + Handler
│   │   │   ├── CreateTarget.cs             # Command + Handler
│   │   │   ├── ListTargets.cs              # Query + Handler
│   │   │   └── CalculateTargetMatch.cs     # Command + Handler (matching algorithm)
│   │   │
│   │   ├── Analytics/
│   │   │   ├── CalculateActionImpact.cs    # Command + Handler (impact measurement)
│   │   │   ├── GetDashboardSummary.cs      # Query + Handler
│   │   │   ├── GetGameTimeline.cs          # Query + Handler (for charts)
│   │   │   └── GetRegionalRevenue.cs       # Query + Handler (geo analysis)
│   │   │
│   │   └── _Common/                        # Shared DTOs, extensions (minimal)
│   │       ├── PaginationDto.cs
│   │       └── ResultDto.cs
│   │
│   ├── Infrastructure/                     # Shared infrastructure (DB, APIs, Jobs)
│   │   ├── Database/
│   │   │   ├── CatoDbContext.cs            # EF Core DbContext
│   │   │   ├── Migrations/                 # EF migrations
│   │   │   └── Configurations/             # Entity configurations (optional)
│   │   │       ├── GameConfiguration.cs
│   │   │       └── ActionConfiguration.cs
│   │   │
│   │   ├── Steam/
│   │   │   ├── ISteamApiService.cs         # Interface
│   │   │   ├── SteamApiService.cs          # Implementation (HTTP client)
│   │   │   └── Models/                     # Steam API response DTOs
│   │   │       └── SteamStoreResponse.cs
│   │   │
│   │   ├── Hangfire/
│   │   │   └── HangfireConfig.cs           # Hangfire setup
│   │   │
│   │   └── Logging/
│   │       └── SerilogConfig.cs            # Structured logging setup
│   │
│   └── Domain/                             # Core entities (anemic models OK)
│       ├── Game.cs                         # Entity
│       ├── GameGenre.cs
│       ├── GenreTag.cs
│       ├── Action.cs
│       ├── GameAction.cs
│       ├── MarketingTarget.cs
│       ├── ActionTarget.cs
│       ├── ActionImpact.cs
│       ├── SteamSaleFinancial.cs
│       ├── SteamTraffic.cs
│       ├── CCUHistory.cs
│       ├── WishlistRankHistory.cs
│       └── LegalEntity.cs
│
├── Cato.Contracts/                         # (Optional) Shared DTOs for API consumers
│   └── GameDto.cs
│
└── Cato.Tests/                             # Unit & Integration tests
    ├── Features/
    │   └── Games/
    │       └── CreateGameTests.cs
    └── Integration/
        └── DatabaseTests.cs
```

---

## Database Schema

Based on the PDF specification, here's the complete schema:

### Core Game Tables

#### MAIN_GAME
Primary table for all games (owned, competitors, sourcing).

```sql
CREATE TABLE main_game (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id INTEGER UNIQUE NOT NULL,
    name VARCHAR(500) NOT NULL,
    game_type VARCHAR(50) NOT NULL, -- 'Owned', 'Competitor', 'Sourcing'
    release_date DATE,
    price_usd DECIMAL(10,2),
    discount_percent INTEGER DEFAULT 0,
    developer_id UUID REFERENCES legal_entity(id),
    publisher_id UUID REFERENCES legal_entity(id),
    is_early_access BOOLEAN DEFAULT FALSE,
    is_released BOOLEAN DEFAULT FALSE,
    header_image_url TEXT,
    short_description TEXT,
    platforms JSONB, -- {"windows": true, "mac": false, "linux": false}
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_main_game_app_id ON main_game(app_id);
CREATE INDEX idx_main_game_type ON main_game(game_type);
```

#### LEGAL_ENTITY
Developers and publishers.

```sql
CREATE TABLE legal_entity (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(500) NOT NULL,
    entity_type VARCHAR(50) NOT NULL, -- 'Developer', 'Publisher'
    contact_email VARCHAR(255),
    website TEXT,
    created_at TIMESTAMP DEFAULT NOW()
);
```

#### GAME_GENRE
Genre taxonomy (both Steam and internal).

```sql
CREATE TABLE game_genre (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID REFERENCES main_game(id) ON DELETE CASCADE,
    genre_name VARCHAR(200) NOT NULL,
    genre_type VARCHAR(50) NOT NULL, -- 'Primary', 'Secondary'
    source VARCHAR(50) NOT NULL, -- 'Steam', 'Internal'
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_game_genre_game_id ON game_genre(game_id);
```

#### GENRE_TAG
Detailed tag system (mechanics, themes, mood).

```sql
CREATE TABLE genre_tag (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID REFERENCES main_game(id) ON DELETE CASCADE,
    tag_name VARCHAR(200) NOT NULL,
    tag_type VARCHAR(50) NOT NULL, -- 'Genre', 'Subgenre', 'Mechanic', 'Theme', 'Mood'
    weight INTEGER DEFAULT 0, -- Relevance score from Steam
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_genre_tag_game_id ON genre_tag(game_id);
CREATE INDEX idx_genre_tag_name ON genre_tag(tag_name);
```

### Financial & Traffic Tables

#### STEAM_SALE_FINANCIAL
Daily sales data from Steam Partner API.

```sql
CREATE TABLE steam_sale_financial (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID REFERENCES main_game(id) ON DELETE CASCADE,
    sale_date DATE NOT NULL,
    country_code VARCHAR(10),
    sales_units INTEGER DEFAULT 0,
    returns_units INTEGER DEFAULT 0,
    gross_revenue_usd DECIMAL(15,2) DEFAULT 0,
    steam_commission_usd DECIMAL(15,2) DEFAULT 0,
    tax_usd DECIMAL(15,2) DEFAULT 0,
    net_revenue_usd DECIMAL(15,2) DEFAULT 0,
    currency VARCHAR(10),
    discount_id INTEGER,
    sale_type VARCHAR(50), -- 'Normal', 'Steam Sale', 'Publisher Sale'
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(game_id, sale_date, country_code) -- Prevent duplicates
);

CREATE INDEX idx_steam_sale_game_date ON steam_sale_financial(game_id, sale_date);
CREATE INDEX idx_steam_sale_country ON steam_sale_financial(country_code);
```

#### STEAM_TRAFFIC
Store page traffic from Steamworks.

```sql
CREATE TABLE steam_traffic (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID REFERENCES main_game(id) ON DELETE CASCADE,
    traffic_date DATE NOT NULL,
    store_page_visits INTEGER DEFAULT 0,
    impressions INTEGER DEFAULT 0,
    click_through_rate DECIMAL(5,2) DEFAULT 0,
    wishlist_additions INTEGER DEFAULT 0,
    purchase_conversion_rate DECIMAL(5,2) DEFAULT 0,
    traffic_source VARCHAR(100), -- 'Discovery Queue', 'Search', 'Event', etc.
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(game_id, traffic_date, traffic_source)
);

CREATE INDEX idx_steam_traffic_game_date ON steam_traffic(game_id, traffic_date);
```

### Marketing Tables

#### MARKETING_TARGET
Influencers, events, media contacts.

```sql
CREATE TABLE marketing_target (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(500) NOT NULL,
    target_type VARCHAR(50) NOT NULL, -- 'Influencer', 'Media', 'Event'
    contact_email VARCHAR(255),
    preferred_genres JSONB, -- ["FPS", "Strategy"]
    preferred_tags JSONB, -- ["Roguelike", "Co-op"]
    audience_size INTEGER,
    platform VARCHAR(100), -- 'Twitch', 'YouTube', 'Twitter', etc.
    notes TEXT,
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_marketing_target_type ON marketing_target(target_type);
```

#### ACTION
Marketing actions (campaigns, discounts, etc.).

```sql
CREATE TABLE action (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_type VARCHAR(50) NOT NULL, -- 'Mailing', 'Influencer', 'Event', 'Discount', 'Bundle'
    decision_source VARCHAR(50) DEFAULT 'Manual', -- 'Manual', 'Rule', 'AI'
    status VARCHAR(50) DEFAULT 'Planned', -- 'Planned', 'Outreach', 'Executed', 'Completed', 'Cancelled'
    action_date DATE, -- When the action was executed
    description TEXT,
    notes TEXT,
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_action_type ON action(action_type);
CREATE INDEX idx_action_date ON action(action_date);
```

#### GAME_ACTION
Many-to-many: actions applied to games.

```sql
CREATE TABLE game_action (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_id UUID REFERENCES action(id) ON DELETE CASCADE,
    game_id UUID REFERENCES main_game(id) ON DELETE CASCADE,
    game_role VARCHAR(50) DEFAULT 'Primary', -- 'Primary', 'Secondary' (for bundles)
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(action_id, game_id)
);

CREATE INDEX idx_game_action_action ON game_action(action_id);
CREATE INDEX idx_game_action_game ON game_action(game_id);
```

#### ACTION_TARGET
Links actions to targets (who was contacted).

```sql
CREATE TABLE action_target (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_id UUID REFERENCES action(id) ON DELETE CASCADE,
    target_id UUID REFERENCES marketing_target(id) ON DELETE CASCADE,
    outreach_date DATE,
    status VARCHAR(50) DEFAULT 'Contacted', -- 'Contacted', 'Accepted', 'Rejected', 'Live'
    notes TEXT,
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(action_id, target_id)
);

CREATE INDEX idx_action_target_action ON action_target(action_id);
CREATE INDEX idx_action_target_target ON action_target(target_id);
```

#### TARGET_MATCH
Pre-calculated match scores (game-target affinity).

```sql
CREATE TABLE target_match (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID REFERENCES main_game(id) ON DELETE CASCADE,
    target_id UUID REFERENCES marketing_target(id) ON DELETE CASCADE,
    lifecycle_stage VARCHAR(50), -- 'Pre-launch', 'Launch', 'Live'
    relevance_score DECIMAL(5,2) DEFAULT 0, -- 0-100 score
    sample_size INTEGER DEFAULT 0, -- How many actions this is based on
    calculated_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(game_id, target_id, lifecycle_stage)
);

CREATE INDEX idx_target_match_game ON target_match(game_id);
CREATE INDEX idx_target_match_target ON target_match(target_id);
CREATE INDEX idx_target_match_score ON target_match(relevance_score DESC);
```

#### ACTION_IMPACT
Measured impact of actions.

```sql
CREATE TABLE action_impact (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_id UUID REFERENCES action(id) ON DELETE CASCADE,
    measurement_start DATE NOT NULL,
    measurement_end DATE NOT NULL,
    wishlist_change INTEGER DEFAULT 0,
    sales_units_change INTEGER DEFAULT 0,
    revenue_change_usd DECIMAL(15,2) DEFAULT 0,
    traffic_change INTEGER DEFAULT 0,
    conversion_rate_change DECIMAL(5,2) DEFAULT 0,
    notes TEXT,
    calculated_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(action_id) -- One impact record per action
);

CREATE INDEX idx_action_impact_action ON action_impact(action_id);
```

### Competitor Tracking Tables

#### CCU_HISTORY
Concurrent user tracking (every 15 minutes).

```sql
CREATE TABLE ccu_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID REFERENCES main_game(id) ON DELETE CASCADE,
    timestamp TIMESTAMP NOT NULL,
    ccu_count INTEGER NOT NULL,
    source VARCHAR(50) DEFAULT 'Steam API', -- 'Steam API', 'SteamDB'
    created_at TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_ccu_history_game_time ON ccu_history(game_id, timestamp DESC);
```

#### WISHLIST_RANK_HISTORY
SteamDB wishlist rank tracking (daily).

```sql
CREATE TABLE wishlist_rank_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id UUID REFERENCES main_game(id) ON DELETE CASCADE,
    rank_date DATE NOT NULL,
    rank_position INTEGER,
    wishlists_count INTEGER, -- If available from SteamDB
    source VARCHAR(50) DEFAULT 'SteamDB',
    created_at TIMESTAMP DEFAULT NOW(),
    UNIQUE(game_id, rank_date)
);

CREATE INDEX idx_wishlist_rank_game_date ON wishlist_rank_history(game_id, rank_date DESC);
```

---

## Vertical Slice Examples

### Example 1: Create Game Feature

**File:** `Features/Games/CreateGame.cs`

```csharp
// 1. The Request (Command)
public record CreateGameCommand(
    int AppId,
    string? Name = null,
    string GameType = "Owned" // 'Owned', 'Competitor', 'Sourcing'
) : IRequest<Result<GameDto>>;

// 2. The Validator
public class CreateGameCommandValidator : AbstractValidator<CreateGameCommand>
{
    public CreateGameCommandValidator()
    {
        RuleFor(x => x.AppId)
            .GreaterThan(0)
            .WithMessage("AppId must be greater than 0");
        
        RuleFor(x => x.GameType)
            .Must(type => new[] { "Owned", "Competitor", "Sourcing" }.Contains(type))
            .WithMessage("GameType must be Owned, Competitor, or Sourcing");
    }
}

// 3. The Handler (Business Logic + DB Access)
public class CreateGameHandler : IRequestHandler<CreateGameCommand, Result<GameDto>>
{
    private readonly CatoDbContext _db;
    private readonly ISteamApiService _steamApi;
    private readonly ILogger<CreateGameHandler> _logger;

    public CreateGameHandler(CatoDbContext db, ISteamApiService steamApi, ILogger<CreateGameHandler> logger)
    {
        _db = db;
        _steamApi = steamApi;
        _logger = logger;
    }

    public async Task<Result<GameDto>> Handle(CreateGameCommand request, CancellationToken ct)
    {
        // Check if game already exists
        var existingGame = await _db.Games.FirstOrDefaultAsync(g => g.AppId == request.AppId, ct);
        if (existingGame != null)
        {
            return Result<GameDto>.Failure($"Game with AppId {request.AppId} already exists");
        }

        // Enrich from Steam API if name not provided
        string gameName = request.Name ?? "";
        if (string.IsNullOrEmpty(gameName))
        {
            var steamData = await _steamApi.GetAppDetails(request.AppId);
            if (steamData == null)
            {
                return Result<GameDto>.Failure($"Could not fetch game data from Steam for AppId {request.AppId}");
            }
            gameName = steamData.Name;
        }

        // Create entity
        var game = new Game
        {
            Id = Guid.NewGuid(),
            AppId = request.AppId,
            Name = gameName,
            GameType = request.GameType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Games.Add(game);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created game {GameId} - {GameName} (AppId: {AppId})", game.Id, game.Name, game.AppId);

        return Result<GameDto>.Success(game.ToDto());
    }
}

// 4. The Endpoint (Carter module)
public class CreateGameEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/games", async (CreateGameCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess 
                ? Results.Created($"/api/games/{result.Data.Id}", result.Data)
                : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("CreateGame")
        .WithTags("Games")
        .Produces<GameDto>(StatusCodes.Status201Created)
        .Produces<string>(StatusCodes.Status400BadRequest);
    }
}

// 5. The DTO
public record GameDto(
    Guid Id,
    int AppId,
    string Name,
    string GameType,
    DateTime CreatedAt
);

// Extension method for mapping
public static class GameExtensions
{
    public static GameDto ToDto(this Game game) => new(
        game.Id,
        game.AppId,
        game.Name,
        game.GameType,
        game.CreatedAt
    );
}

// Result wrapper
public class Result<T>
{
    public bool IsSuccess { get; init; }
    public T Data { get; init; }
    public string ErrorMessage { get; init; }

    public static Result<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public static Result<T> Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}
```

### Example 2: Calculate Action Impact Feature

**File:** `Features/Analytics/CalculateActionImpact.cs`

```csharp
// 1. Command
public record CalculateActionImpactCommand(Guid ActionId) : IRequest<Result<ActionImpactDto>>;

// 2. Handler
public class CalculateActionImpactHandler : IRequestHandler<CalculateActionImpactCommand, Result<ActionImpactDto>>
{
    private readonly CatoDbContext _db;
    private readonly ILogger<CalculateActionImpactHandler> _logger;

    public CalculateActionImpactHandler(CatoDbContext db, ILogger<CalculateActionImpactHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<ActionImpactDto>> Handle(CalculateActionImpactCommand request, CancellationToken ct)
    {
        // 1. Get the action
        var action = await _db.Actions
            .Include(a => a.GameActions).ThenInclude(ga => ga.Game)
            .FirstOrDefaultAsync(a => a.Id == request.ActionId, ct);

        if (action == null)
        {
            return Result<ActionImpactDto>.Failure($"Action {request.ActionId} not found");
        }

        if (!action.ActionDate.HasValue)
        {
            return Result<ActionImpactDto>.Failure("Action does not have an execution date");
        }

        var actionDate = action.ActionDate.Value;
        var gameIds = action.GameActions.Select(ga => ga.GameId).ToList();

        // 2. Calculate baseline (7 days before)
        var baselineStart = actionDate.AddDays(-7);
        var baselineEnd = actionDate.AddDays(-1);

        var baselineData = await _db.SteamSaleFinancials
            .Where(f => gameIds.Contains(f.GameId) && f.SaleDate >= baselineStart && f.SaleDate <= baselineEnd)
            .GroupBy(f => 1)
            .Select(g => new
            {
                TotalSales = g.Sum(f => f.SalesUnits),
                TotalRevenue = g.Sum(f => f.NetRevenueUsd)
            })
            .FirstOrDefaultAsync(ct);

        // 3. Calculate result (7 days after)
        var resultStart = actionDate.AddDays(1);
        var resultEnd = actionDate.AddDays(7);

        var resultData = await _db.SteamSaleFinancials
            .Where(f => gameIds.Contains(f.GameId) && f.SaleDate >= resultStart && f.SaleDate <= resultEnd)
            .GroupBy(f => 1)
            .Select(g => new
            {
                TotalSales = g.Sum(f => f.SalesUnits),
                TotalRevenue = g.Sum(f => f.NetRevenueUsd)
            })
            .FirstOrDefaultAsync(ct);

        // 4. Calculate changes
        int baselineSales = baselineData?.TotalSales ?? 0;
        decimal baselineRevenue = baselineData?.TotalRevenue ?? 0;
        int resultSales = resultData?.TotalSales ?? 0;
        decimal resultRevenue = resultData?.TotalRevenue ?? 0;

        int salesChange = resultSales - baselineSales;
        decimal revenueChange = resultRevenue - baselineRevenue;

        // 5. Save impact
        var impact = new ActionImpact
        {
            Id = Guid.NewGuid(),
            ActionId = action.Id,
            MeasurementStart = resultStart,
            MeasurementEnd = resultEnd,
            SalesUnitsChange = salesChange,
            RevenueChangeUsd = revenueChange,
            CalculatedAt = DateTime.UtcNow
        };

        // Upsert (update if exists, insert if not)
        var existing = await _db.ActionImpacts.FirstOrDefaultAsync(i => i.ActionId == action.Id, ct);
        if (existing != null)
        {
            existing.SalesUnitsChange = salesChange;
            existing.RevenueChangeUsd = revenueChange;
            existing.CalculatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.ActionImpacts.Add(impact);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Calculated impact for action {ActionId}: Sales change = {SalesChange}, Revenue change = {RevenueChange:C}",
            action.Id, salesChange, revenueChange
        );

        return Result<ActionImpactDto>.Success(impact.ToDto());
    }
}

// 3. Endpoint
public class CalculateActionImpactEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/analytics/actions/{id}/calculate-impact", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new CalculateActionImpactCommand(id));
            return result.IsSuccess 
                ? Results.Ok(result.Data)
                : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("CalculateActionImpact")
        .WithTags("Analytics");
    }
}

// DTO
public record ActionImpactDto(
    Guid Id,
    Guid ActionId,
    int SalesUnitsChange,
    decimal RevenueChangeUsd,
    DateTime CalculatedAt
);
```

---

## Python Collector Integration

### Strategy 1: File-Based Ingestion (Recommended for MVP)

**Python Collector Output Structure:**
```
/shared/data/
├── games/
│   └── 2026-02-06/
│       ├── 730/  # CS:GO
│       │   ├── steam_store_details.json
│       │   ├── steam_news.json
│       │   └── steamworks_wishlists.csv
│       └── 570/  # Dota 2
│           └── ...
├── financials/
│   ├── 730_2026_02_06_financial.json
│   └── highwatermark.json
└── steamdb/
    ├── mostwished.csv
    └── wishlist_activity.csv
```

**.NET Ingestion Feature:**

```csharp
// Features/Ingestion/IngestFinancialData.cs

public record IngestFinancialDataCommand(string FilePath) : IRequest<Result<int>>;

public class IngestFinancialDataHandler : IRequestHandler<IngestFinancialDataCommand, Result<int>>
{
    private readonly CatoDbContext _db;
    private readonly ILogger<IngestFinancialDataHandler> _logger;

    public async Task<Result<int>> Handle(IngestFinancialDataCommand request, CancellationToken ct)
    {
        if (!File.Exists(request.FilePath))
        {
            return Result<int>.Failure($"File not found: {request.FilePath}");
        }

        var jsonContent = await File.ReadAllTextAsync(request.FilePath, ct);
        var pythonData = JsonSerializer.Deserialize<PythonFinancialOutput>(jsonContent);

        int recordsInserted = 0;

        foreach (var transaction in pythonData.Transactions)
        {
            // Find game by AppId
            var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == transaction.AppId, ct);
            if (game == null)
            {
                _logger.LogWarning("Game with AppId {AppId} not found, skipping transaction", transaction.AppId);
                continue;
            }

            // Upsert financial record (prevent duplicates)
            var existing = await _db.SteamSaleFinancials
                .FirstOrDefaultAsync(f => 
                    f.GameId == game.Id && 
                    f.SaleDate == transaction.Date && 
                    f.CountryCode == transaction.CountryCode, ct);

            if (existing != null)
            {
                // Update existing record
                existing.SalesUnits = transaction.GrossUnitsSold;
                existing.NetRevenueUsd = transaction.NetSalesUsd;
                // ... update other fields
            }
            else
            {
                // Insert new record
                var financialRecord = new SteamSaleFinancial
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    SaleDate = transaction.Date,
                    CountryCode = transaction.CountryCode,
                    SalesUnits = transaction.GrossUnitsSold,
                    NetRevenueUsd = transaction.NetSalesUsd,
                    // ... map other fields
                    CreatedAt = DateTime.UtcNow
                };
                _db.SteamSaleFinancials.Add(financialRecord);
                recordsInserted++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Ingested {Count} financial records from {FilePath}", recordsInserted, request.FilePath);

        return Result<int>.Success(recordsInserted);
    }
}
```

### Strategy 2: REST API Integration

**Python Collector Posts Data:**

```python
# In Python collector
import requests

def send_to_dotnet_api(financial_data):
    url = "http://localhost:5000/api/ingestion/financial"
    headers = {"Content-Type": "application/json"}
    response = requests.post(url, json=financial_data, headers=headers)
    return response.status_code == 200
```

**.NET Endpoint:**

```csharp
// Features/Ingestion/IngestFinancialData.cs (modified for POST)

public class IngestFinancialDataEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ingestion/financial", async (PythonFinancialOutput data, ISender sender) =>
        {
            var command = new IngestFinancialDataFromApiCommand(data);
            var result = await sender.Send(command);
            return result.IsSuccess 
                ? Results.Ok(new { RecordsIngested = result.Data })
                : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("IngestFinancialDataAPI")
        .WithTags("Ingestion");
    }
}
```

---

## API Design

### API Structure

```
/api/games
  GET    /                    # List games (with pagination, filters)
  POST   /                    # Create game
  GET    /{id}                # Get game details
  PATCH  /{id}                # Update game
  DELETE /{id}                # Delete game (soft delete recommended)
  POST   /{id}/enrich         # Fetch from Steam and update

/api/competitors
  POST   /                    # Add competitor game
  GET    /                    # List competitors
  GET    /{id}/ccu-history    # Get CCU time series
  GET    /{id}/wishlist-rank  # Get wishlist rank history

/api/marketing/targets
  GET    /                    # List targets (influencers, events)
  POST   /                    # Create target
  GET    /{id}                # Get target details
  PATCH  /{id}                # Update target

/api/marketing/actions
  GET    /                    # List actions
  POST   /                    # Create action
  GET    /{id}                # Get action details
  PATCH  /{id}                # Update action
  POST   /{id}/link-game      # Link game to action
  POST   /{id}/link-target    # Link target to action

/api/analytics
  POST   /actions/{id}/calculate-impact  # Calculate impact for action
  GET    /dashboard/summary              # Dashboard KPIs
  GET    /games/{id}/timeline            # CCU/wishlist time series + action markers
  GET    /revenue/regional               # Regional revenue report

/api/ingestion
  POST   /financial           # Ingest financial data (from Python or file)
  POST   /wishlist            # Ingest wishlist data
  POST   /steamdb             # Ingest SteamDB data
  POST   /gamalytic           # Ingest Gamalytic data

/health                       # Health check endpoint
/hangfire                     # Hangfire dashboard (admin only)
```

### API Conventions

- **HTTP Verbs:** GET (read), POST (create), PATCH (update), DELETE (remove)
- **Status Codes:**
  - `200 OK` - Success
  - `201 Created` - Resource created
  - `400 Bad Request` - Validation error
  - `404 Not Found` - Resource not found
  - `500 Internal Server Error` - Unhandled exception
- **Pagination:** Use query params `?page=1&pageSize=20`
- **Filtering:** Use query params `?gameType=Competitor&releasedOnly=true`
- **Sorting:** Use query param `?sortBy=releaseDate&sortOrder=desc`

---

## Background Jobs

### Hangfire Configuration

**File:** `Infrastructure/Hangfire/HangfireConfig.cs`

```csharp
public static class HangfireConfig
{
    public static IServiceCollection AddHangfireServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(config.GetConnectionString("DefaultConnection")));

        services.AddHangfireServer();

        return services;
    }

    public static IApplicationBuilder UseHangfireJobs(this IApplicationBuilder app)
    {
        // Recurring jobs
        RecurringJob.AddOrUpdate<TrackCCUJob>(
            "track-ccu",
            job => job.Execute(),
            "*/15 * * * *"); // Every 15 minutes

        RecurringJob.AddOrUpdate<DailyDataIngestionJob>(
            "daily-ingestion",
            job => job.Execute(),
            "0 4 * * *"); // Daily at 04:00 UTC

        return app;
    }
}
```

### Job Examples

**CCU Tracking Job:**

```csharp
// Features/Competitors/TrackCCUJob.cs

public class TrackCCUJob
{
    private readonly CatoDbContext _db;
    private readonly ISteamApiService _steamApi;
    private readonly ILogger<TrackCCUJob> _logger;

    public TrackCCUJob(CatoDbContext db, ISteamApiService steamApi, ILogger<TrackCCUJob> logger)
    {
        _db = db;
        _steamApi = steamApi;
        _logger = logger;
    }

    public async Task Execute()
    {
        _logger.LogInformation("Starting CCU tracking job");

        var trackedGames = await _db.Games
            .Where(g => g.GameType == "Competitor" || g.GameType == "Owned")
            .ToListAsync();

        foreach (var game in trackedGames)
        {
            try
            {
                var ccuData = await _steamApi.GetCurrentPlayers(game.AppId);
                if (ccuData == null) continue;

                var ccuRecord = new CCUHistory
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    Timestamp = DateTime.UtcNow,
                    CCUCount = ccuData.PlayerCount,
                    Source = "Steam API",
                    CreatedAt = DateTime.UtcNow
                };

                _db.CCUHistories.Add(ccuRecord);
                _logger.LogDebug("Recorded CCU for game {GameId}: {CCU}", game.Id, ccuData.PlayerCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track CCU for game {GameId}", game.Id);
            }

            // Rate limiting
            await Task.Delay(200); // 200ms delay between requests
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("CCU tracking job completed for {Count} games", trackedGames.Count);
    }
}
```

**Daily Ingestion Job:**

```csharp
// Features/Ingestion/DailyDataIngestionJob.cs

public class DailyDataIngestionJob
{
    private readonly ISender _mediator;
    private readonly ILogger<DailyDataIngestionJob> _logger;
    private readonly IConfiguration _config;

    public async Task Execute()
    {
        _logger.LogInformation("Starting daily data ingestion job");

        try
        {
            // 1. Trigger Python collectors (optional - or assume they run via cron)
            // await TriggerPythonCollectors();

            // 2. Ingest financial data
            var financialPath = Path.Combine(_config["DataPath"], "financials", $"*_{DateTime.UtcNow:yyyy_MM_dd}_financial.json");
            await _mediator.Send(new IngestFinancialDataCommand(financialPath));

            // 3. Ingest wishlist data
            // await _mediator.Send(new IngestWishlistDataCommand(...));

            // 4. Ingest SteamDB data
            // await _mediator.Send(new IngestSteamDbDataCommand(...));

            _logger.LogInformation("Daily data ingestion job completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily data ingestion job failed");
            // Send alert (Discord/Slack webhook)
            await SendFailureAlert(ex.Message);
        }
    }

    private async Task SendFailureAlert(string errorMessage)
    {
        // Discord webhook example
        var webhookUrl = _config["Discord:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl)) return;

        var payload = new { content = $"🚨 **CATO Backend Alert**\nDaily ingestion failed: {errorMessage}" };
        using var httpClient = new HttpClient();
        await httpClient.PostAsJsonAsync(webhookUrl, payload);
    }
}
```

---

## Security & Configuration

### Configuration Management

**Development (User Secrets):**

```bash
dotnet user-secrets init
dotnet user-secrets set "Steam:ApiKey" "YOUR_KEY_HERE"
dotnet user-secrets set "Steam:FinancialApiKey" "YOUR_FINANCIAL_KEY"
dotnet user-secrets set "Gamalytic:ApiKey" "YOUR_GAMALYTIC_KEY"
dotnet user-secrets set "Discord:WebhookUrl" "YOUR_WEBHOOK_URL"
```

**Production (Environment Variables or Azure Key Vault):**

```bash
export Steam__ApiKey="YOUR_KEY"
export ConnectionStrings__DefaultConnection="Host=db;Database=cato;..."
```

**appsettings.json:**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=cato;Username=cato_user;Password=dev_password"
  },
  "Steam": {
    "ApiKey": "",
    "FinancialApiKey": "",
    "RateLimitDelayMs": 200
  },
  "Gamalytic": {
    "ApiKey": ""
  },
  "DataPath": "/shared/data",
  "Hangfire": {
    "DashboardPath": "/hangfire"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

### Authentication & Authorization (Future)

For MVP, API can be internal/unauthenticated. For production:

- **JWT Authentication:** Use `Microsoft.AspNetCore.Authentication.JwtBearer`
- **API Keys:** Custom middleware for API key validation
- **Role-Based Access:** Admin vs. Read-Only users

---

## Deployment Architecture

### Development Setup

```
┌────────────────────────────────────────┐
│  Local Machine                         │
│  ┌──────────────────────────────────┐  │
│  │  .NET API (Kestrel)              │  │
│  │  http://localhost:5000           │  │
│  └──────────────┬───────────────────┘  │
│                 │                       │
│  ┌──────────────▼───────────────────┐  │
│  │  PostgreSQL (Docker)             │  │
│  │  localhost:5432                  │  │
│  └──────────────────────────────────┘  │
│                                        │
│  ┌──────────────────────────────────┐  │
│  │  Python Collectors (Conda env)   │  │
│  │  Output: ./data/                 │  │
│  └──────────────────────────────────┘  │
└────────────────────────────────────────┘
```

### Production Setup (Cloud)

```
┌───────────────────────────────────────────────────────┐
│  Cloud (Azure / AWS / DigitalOcean)                   │
│                                                        │
│  ┌────────────────────────────────────────────────┐   │
│  │  .NET API (Docker container)                   │   │
│  │  - Exposed via HTTPS (reverse proxy)           │   │
│  │  - Environment: Production                     │   │
│  └────────────────┬───────────────────────────────┘   │
│                   │                                    │
│  ┌────────────────▼───────────────────────────────┐   │
│  │  PostgreSQL (Managed DB)                       │   │
│  │  - Automated backups                           │   │
│  │  - Read replicas (optional)                    │   │
│  └────────────────────────────────────────────────┘   │
│                                                        │
│  ┌────────────────────────────────────────────────┐   │
│  │  Python Collectors (Docker container or cron)  │   │
│  │  - Scheduled via Hangfire or OS cron           │   │
│  │  - Shared volume for data exchange             │   │
│  └────────────────────────────────────────────────┘   │
│                                                        │
│  ┌────────────────────────────────────────────────┐   │
│  │  Reverse Proxy (Nginx / Caddy)                 │   │
│  │  - SSL/TLS termination                         │   │
│  │  - HTTPS redirect                              │   │
│  └────────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────┘
```

### Docker Compose Example

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:14
    environment:
      POSTGRES_DB: cato
      POSTGRES_USER: cato_user
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"

  api:
    build:
      context: ./Cato.API
      dockerfile: Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=cato;Username=cato_user;Password=${DB_PASSWORD}"
      Steam__ApiKey: ${STEAM_API_KEY}
      Steam__FinancialApiKey: ${STEAM_FINANCIAL_API_KEY}
    ports:
      - "5000:80"
    depends_on:
      - postgres
    volumes:
      - ./shared/data:/app/data

  python-collectors:
    build:
      context: ./catoptric-data-collector
      dockerfile: Dockerfile
    environment:
      STEAM_API_KEY: ${STEAM_API_KEY}
      STEAM_FINANCIAL_API_KEY: ${STEAM_FINANCIAL_API_KEY}
      GAMALYTIC_API_KEY: ${GAMALYTIC_API_KEY}
    volumes:
      - ./shared/data:/data
    # Run collectors on a schedule (using cron inside container or Hangfire from .NET)

volumes:
  postgres_data:
```

---

## Summary

This architecture provides:

1. **Vertical Slice** organization for maintainability
2. **Integration** with existing Python collectors (no rewrite needed)
3. **PostgreSQL** as single source of truth
4. **Hangfire** for automated jobs (CCU tracking, daily ingestion)
5. **MediatR + Carter** for clean request handling
6. **Polly** for resilience (rate limiting, retries)
7. **Clear separation** between features, infrastructure, and domain

**Next Steps:**
1. Initialize .NET projects
2. Design EF Core entity models
3. Create initial migration
4. Implement first vertical slice (Create Game)
5. Test end-to-end flow

This architecture will scale from MVP to production with minimal refactoring.
