# CATO Backend Implementation Guide
## Quick Start Guide for Development

**Last Updated:** February 6, 2026

This guide helps you get started with implementing the CATO backend based on the comprehensive roadmap and architecture documents.

---

## 📚 Documentation Structure

1. **`backend_roadmap_v2.md`** - Complete development roadmap with phases, timelines, and success metrics
2. **`dotnet_architecture_v2.md`** - Technical architecture, code examples, and database schema
3. **`IMPLEMENTATION_GUIDE.md`** (this file) - Quick start and step-by-step instructions

---

## 🎯 What You're Building

A .NET backend that:
- **Stores** all game data (owned, competitors, sourcing targets) in PostgreSQL
- **Integrates** existing Python data collectors for Steam, SteamDB, Gamalytic
- **Tracks** competitor CCU and wishlist ranks automatically
- **Manages** marketing actions (influencer outreach, mailings, events)
- **Calculates** ROI/impact of marketing efforts
- **Provides** analytics API for dashboards

---

## 🚀 Quick Start (First Day)

### Prerequisites
- .NET 8 SDK installed
- PostgreSQL 14+ (local or Docker)
- Python 3.12 with existing collectors working
- IDE: Visual Studio, VS Code, or Rider

### Step 1: Initialize Solution (15 minutes)

```bash
# Create solution
mkdir Cato
cd Cato
dotnet new sln -n Cato

# Create projects
dotnet new webapi -n Cato.API
dotnet new classlib -n Cato.Domain
dotnet new classlib -n Cato.Infrastructure

# Add to solution
dotnet sln add Cato.API/Cato.API.csproj
dotnet sln add Cato.Domain/Cato.Domain.csproj
dotnet sln add Cato.Infrastructure/Cato.Infrastructure.csproj

# Add project references
cd Cato.API
dotnet add reference ../Cato.Domain/Cato.Domain.csproj
dotnet add reference ../Cato.Infrastructure/Cato.Infrastructure.csproj

cd ../Cato.Infrastructure
dotnet add reference ../Cato.Domain/Cato.Domain.csproj
```

### Step 2: Install NuGet Packages (10 minutes)

```bash
cd ../Cato.API

# Database
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design

# Vertical Slice Architecture
dotnet add package MediatR
dotnet add package Carter
dotnet add package FluentValidation
dotnet add package FluentValidation.DependencyInjectionExtensions

# Background Jobs
dotnet add package Hangfire
dotnet add package Hangfire.PostgreSql

# Resilience
dotnet add package Polly

# Logging
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
```

### Step 3: Setup Database Connection (10 minutes)

Create `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=cato;Username=cato_user;Password=dev_password;Port=5432"
  },
  "Steam": {
    "ApiKey": "",
    "FinancialApiKey": "",
    "RateLimitDelayMs": 200
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

Setup user secrets:

```bash
dotnet user-secrets init
dotnet user-secrets set "Steam:ApiKey" "YOUR_STEAM_API_KEY"
dotnet user-secrets set "Steam:FinancialApiKey" "YOUR_FINANCIAL_KEY"
```

Create PostgreSQL database:

```sql
CREATE DATABASE cato;
CREATE USER cato_user WITH PASSWORD 'dev_password';
GRANT ALL PRIVILEGES ON DATABASE cato TO cato_user;
```

### Step 4: Create First Entity & DbContext (20 minutes)

**File:** `Cato.Domain/Game.cs`

```csharp
namespace Cato.Domain;

public class Game
{
    public Guid Id { get; set; }
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GameType { get; set; } = "Owned"; // Owned, Competitor, Sourcing
    public DateTime? ReleaseDate { get; set; }
    public decimal? PriceUsd { get; set; }
    public string? DeveloperName { get; set; }
    public string? PublisherName { get; set; }
    public bool IsEarlyAccess { get; set; }
    public bool IsReleased { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**File:** `Cato.Infrastructure/Database/CatoDbContext.cs`

```csharp
using Cato.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cato.Infrastructure.Database;

public class CatoDbContext : DbContext
{
    public CatoDbContext(DbContextOptions<CatoDbContext> options) : base(options)
    {
    }

    public DbSet<Game> Games => Set<Game>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("main_game");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppId).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(500).IsRequired();
            entity.Property(e => e.GameType).HasMaxLength(50).IsRequired();
        });
    }
}
```

### Step 5: Create First Migration (5 minutes)

```bash
cd Cato.Infrastructure
dotnet ef migrations add InitialCreate --startup-project ../Cato.API
dotnet ef database update --startup-project ../Cato.API
```

### Step 6: Setup Program.cs (15 minutes)

**File:** `Cato.API/Program.cs`

```csharp
using Carter;
using Cato.Infrastructure.Database;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Database
builder.Services.AddDbContext<CatoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// MediatR
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// Carter (endpoints)
builder.Services.AddCarter();

// OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.MapCarter();

app.Run();
```

### Step 7: Create First Feature (30 minutes)

**File:** `Cato.API/Features/Games/CreateGame.cs`

```csharp
using Carter;
using Cato.Domain;
using Cato.Infrastructure.Database;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Games;

// Command
public record CreateGameCommand(int AppId, string Name, string GameType = "Owned") 
    : IRequest<Result<GameResponse>>;

// Validator
public class CreateGameCommandValidator : AbstractValidator<CreateGameCommand>
{
    public CreateGameCommandValidator()
    {
        RuleFor(x => x.AppId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(500);
        RuleFor(x => x.GameType).Must(x => new[] { "Owned", "Competitor", "Sourcing" }.Contains(x));
    }
}

// Handler
public class CreateGameHandler : IRequestHandler<CreateGameCommand, Result<GameResponse>>
{
    private readonly CatoDbContext _db;
    private readonly ILogger<CreateGameHandler> _logger;

    public CreateGameHandler(CatoDbContext db, ILogger<CreateGameHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<GameResponse>> Handle(CreateGameCommand request, CancellationToken ct)
    {
        // Check if exists
        if (await _db.Games.AnyAsync(g => g.AppId == request.AppId, ct))
        {
            return Result<GameResponse>.Failure($"Game with AppId {request.AppId} already exists");
        }

        // Create entity
        var game = new Game
        {
            Id = Guid.NewGuid(),
            AppId = request.AppId,
            Name = request.Name,
            GameType = request.GameType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Games.Add(game);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created game {GameId} - {GameName}", game.Id, game.Name);

        return Result<GameResponse>.Success(GameResponse.FromEntity(game));
    }
}

// Endpoint
public class CreateGameEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/games", async (CreateGameCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess 
                ? Results.Created($"/api/games/{result.Data!.Id}", result.Data)
                : Results.BadRequest(new { error = result.ErrorMessage });
        })
        .WithName("CreateGame")
        .WithTags("Games");
    }
}

// DTOs
public record GameResponse(Guid Id, int AppId, string Name, string GameType, DateTime CreatedAt)
{
    public static GameResponse FromEntity(Game game) => new(
        game.Id, game.AppId, game.Name, game.GameType, game.CreatedAt
    );
}

// Result wrapper
public class Result<T>
{
    public bool IsSuccess { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }

    public static Result<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public static Result<T> Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}
```

### Step 8: Test It! (5 minutes)

```bash
cd Cato.API
dotnet run
```

Open browser: `http://localhost:5000/swagger`

Test the endpoint:

```bash
curl -X POST http://localhost:5000/api/games \
  -H "Content-Type: application/json" \
  -d '{"appId": 730, "name": "Counter-Strike: Global Offensive", "gameType": "Competitor"}'
```

---

## 📋 Next Steps (Week 1)

### Day 1 (Completed Above)
- [x] Initialize solution
- [x] Setup database
- [x] Create first entity & migration
- [x] Create first feature (CreateGame)
- [x] Test endpoint

### Day 2: Complete Game CRUD
- [ ] Add `GetGameDetails.cs` (GET /api/games/{id})
- [ ] Add `ListGames.cs` (GET /api/games with filtering)
- [ ] Add `UpdateGame.cs` (PATCH /api/games/{id})
- [ ] Test all endpoints

### Day 3: Steam API Integration
- [ ] Create `ISteamApiService` interface
- [ ] Implement `SteamApiService` (HTTP client)
- [ ] Add `EnrichGameFromSteam.cs` feature
- [ ] Test enrichment with real Steam API

### Day 4: Add More Tables
- [ ] Create `LegalEntity` entity (developers/publishers)
- [ ] Create `GameGenre` entity
- [ ] Create `GenreTag` entity
- [ ] Create migration
- [ ] Update `Game` entity with relationships

### Day 5: Python Collector Integration
- [ ] Create `IngestFinancialData.cs` feature
- [ ] Test reading Python JSON outputs
- [ ] Create `SteamSaleFinancial` entity
- [ ] Test ingestion pipeline

---

## 📊 Phase Checklist

Use this to track progress through the roadmap phases:

### Phase 0: Assessment & Setup
- [x] Review Python collectors
- [x] Review PDF specification
- [x] Create roadmap v2
- [x] Create architecture v2
- [ ] Database schema design (complete all tables)
- [ ] Initialize .NET project ✅ (partially done above)
- [ ] Install core packages ✅
- [ ] Setup development environment ✅

### Phase 1: Foundation & Core Tables (Target: 3-5 days)
- [ ] Implement all core entities (Game, Genre, Tag, LegalEntity)
- [ ] Create all migrations
- [ ] Implement Game CRUD features
- [ ] Implement Steam enrichment
- [ ] Test end-to-end

### Phase 2: Data Ingestion (Target: 5-7 days)
- [ ] Create financial ingestion feature
- [ ] Create wishlist ingestion feature
- [ ] Create SteamDB ingestion feature
- [ ] Create Gamalytic ingestion feature
- [ ] Setup Hangfire for daily jobs
- [ ] Test idempotency

### Phase 3: Competitor Tracking (Target: 3-4 days)
- [ ] Add CCU tracking table & feature
- [ ] Add wishlist rank table & feature
- [ ] Implement 15-minute CCU job
- [ ] Implement daily wishlist rank job
- [ ] Test competitor workflow

### Phase 4: Marketing Actions (Target: 5-7 days)
- [ ] Implement Action CRUD
- [ ] Implement Target CRUD
- [ ] Implement Game-Action linking
- [ ] Implement Target-Action linking
- [ ] Implement target match algorithm
- [ ] Test marketing workflow

### Phase 5: Impact Analysis (Target: 5-7 days) ⭐ MVP COMPLETE
- [ ] Implement impact calculation feature
- [ ] Implement dashboard summary
- [ ] Implement game timeline query
- [ ] Implement regional revenue report
- [ ] Test impact measurement
- [ ] Validate ROI calculations

---

## 🛠️ Common Commands Reference

### Database Commands
```bash
# Add migration
dotnet ef migrations add MigrationName --startup-project Cato.API --project Cato.Infrastructure

# Apply migration
dotnet ef database update --startup-project Cato.API --project Cato.Infrastructure

# Rollback migration
dotnet ef database update PreviousMigrationName --startup-project Cato.API --project Cato.Infrastructure

# Generate SQL script
dotnet ef migrations script --startup-project Cato.API --project Cato.Infrastructure --output migration.sql
```

### Run & Debug
```bash
# Run API
cd Cato.API
dotnet run

# Watch mode (auto-reload)
dotnet watch run

# Build only
dotnet build

# Clean build
dotnet clean && dotnet build
```

### Python Collector Integration
```bash
# Run Python collectors (from their directory)
cd ../catoptric-data-collector
python data_collector.py --test

# Output will be in: data/games/YYYY-MM-DD/
```

---

## 🐛 Troubleshooting

### Database connection fails
- Check PostgreSQL is running: `pg_isready`
- Verify connection string in `appsettings.Development.json`
- Check database exists: `psql -U cato_user -d cato`

### Migrations fail
- Ensure you're in the correct directory
- Check `--startup-project` and `--project` flags are correct
- Delete `Migrations/` folder and start fresh if needed

### Carter endpoints not found
- Make sure `app.MapCarter()` is in `Program.cs`
- Check your feature class implements `ICarterModule`
- Rebuild solution: `dotnet clean && dotnet build`

### MediatR handler not found
- Verify handler class implements `IRequestHandler<TRequest, TResponse>`
- Check `AddMediatR` registration includes correct assembly
- Rebuild solution

---

## 📖 Best Practices

### Vertical Slice Development
1. **One feature = one file** (unless it gets huge, then split into folder)
2. **Include everything in the slice:** Command/Query, Validator, Handler, Endpoint, DTOs
3. **Keep infrastructure separate:** DbContext, external APIs, jobs in `Infrastructure/`

### Database
1. **Always use migrations** - Never modify database manually
2. **Name migrations descriptively:** `AddActionImpactTable` not `Update1`
3. **Test migrations on fresh database** before committing
4. **Use indexes** for foreign keys and frequently queried columns

### Testing Strategy
1. **Manual test first** with Swagger/Postman
2. **Integration tests** for critical paths (ingestion, impact calculation)
3. **Unit tests** for complex business logic (target matching algorithm)

### Git Workflow
1. **Feature branches:** `feature/game-crud`, `feature/ccu-tracking`
2. **Commit frequently** with clear messages
3. **PR/review** before merging to main (even solo dev - for discipline)

---

## 🎓 Learning Resources

### Vertical Slice Architecture
- Derek Comartin: [Vertical Slice Architecture](https://codeopinion.com/vertical-slice-architecture/)
- Jimmy Bogard: [CQRS with MediatR](https://github.com/jbogard/MediatR)

### Entity Framework Core
- [EF Core Documentation](https://docs.microsoft.com/en-us/ef/core/)
- [Migrations Overview](https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/)

### Carter
- [Carter GitHub](https://github.com/CarterCommunity/Carter)
- [Minimal APIs with Carter](https://www.youtube.com/watch?v=6HY2nQiAFIQ)

---

## 🎯 Success Criteria

### MVP Complete When...
- ✅ Can add games manually or via AppID (auto-enrich from Steam)
- ✅ Python collector outputs successfully ingest to PostgreSQL
- ✅ CCU tracking runs every 15 minutes automatically
- ✅ Can create marketing actions linked to games and targets
- ✅ Impact calculation shows before/after metrics for actions
- ✅ Dashboard API returns actionable insights

### Ready for Production When...
- ✅ All MVP features working
- ✅ Error logging and alerting configured
- ✅ Hangfire jobs stable and monitored
- ✅ Database backups automated
- ✅ API documentation complete (Swagger)
- ✅ Docker deployment tested
- ✅ Admin UI implemented (Phase 7)

---

## 📞 Need Help?

If you get stuck:
1. Check the architecture doc (`dotnet_architecture_v2.md`) for code examples
2. Check the roadmap (`backend_roadmap_v2.md`) for feature specifications
3. Review the PDF spec for business logic requirements
4. Search for error messages (Stack Overflow, GitHub issues)
5. Ask in .NET Discord/communities

---

**Good luck! You're building something awesome.** 🚀
