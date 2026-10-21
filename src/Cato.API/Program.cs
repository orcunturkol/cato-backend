using Cato.API.Health;
using Cato.API.Hosting;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.Filtering;
using Cato.Infrastructure.Steam.SteamKit;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Process role (CATO_ROLE: all | api | worker | migrate; see Hosting/CatoRole.cs) ──
var role = CatoRoles.Parse(builder.Configuration[CatoRoles.ConfigurationKey]);

// ── Serilog ──
// Sinks + minimum levels are configured entirely via the "Serilog" section in
// appsettings.json (ReadFrom.Configuration), so log hygiene and the Seq sink can
// be tuned without code changes. Enrichers/console are declared there too.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("Role", role.ToString().ToLowerInvariant())
    .CreateLogger();

builder.Host.UseSerilog();

// ── Database ──
builder.Services.AddDbContext<CatoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── MediatR ──
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// ── FluentValidation ──
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// ── Controllers ──
builder.Services.AddControllers().ForRole(role);

// ── Health checks (/health/live, /health/ready) ──
// The SteamKit check only belongs where SteamKit runs; the api role never logs in.
builder.Services.AddCatoHealthChecks(includeSteamKit: role.RunsHostedServices());

// ── Steam API HttpClient ──
builder.Services.AddHttpClient<ISteamApiService, SteamApiService>(client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ── Redis ──
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var cfg = builder.Configuration.GetSection("Redis").Get<RedisSettings>() ?? new RedisSettings();
    return ConnectionMultiplexer.Connect(cfg.ConnectionString);
});
builder.Services.AddSingleton<IRedisAppIdSyncService, RedisAppIdSyncService>();
builder.Services.AddSingleton<ISteamIdRotationService, RedisSteamIdRotationService>();
builder.Services.AddSingleton<IFollowerHistoryQueueService, FollowerHistoryQueueService>();
builder.Services.Configure<FollowerHistorySweepSettings>(builder.Configuration.GetSection(FollowerHistorySweepSettings.SectionName));

// ── RabbitMQ ──
builder.Services.Configure<Cato.Infrastructure.Messaging.RabbitMqSettings>(
    builder.Configuration.GetSection("RabbitMQ"));
// The broker's connection list names the process: cato-worker, cato-all, ...
builder.Services.PostConfigure<Cato.Infrastructure.Messaging.RabbitMqSettings>(s =>
    s.ClientProvidedName = $"cato-{role.ToString().ToLowerInvariant()}");
builder.Services.AddScoped<Cato.Infrastructure.Messaging.IIngestionDispatcher, Cato.API.Services.IngestionDispatcher>();
builder.Services.AddScoped<Cato.Infrastructure.Messaging.IBatchIngestionDispatcher, Cato.API.Services.BatchIngestionDispatcher>();
builder.Services.AddScoped<Cato.Infrastructure.Messaging.IGameAnalyzedDispatcher, Cato.API.Services.GameAnalyzedDispatcher>();

// ── Game quality filter ──
builder.Services.Configure<GameFilterOptions>(builder.Configuration.GetSection(GameFilterOptions.SectionName));
builder.Services.AddSingleton<IGameQualityFilter, GameQualityFilterService>();

// ── Steam Web API + player profile watcher ──
builder.Services.Configure<SteamWebApiSettings>(builder.Configuration.GetSection(SteamWebApiSettings.SectionName));
builder.Services.Configure<PlayerProfileSettings>(builder.Configuration.GetSection(PlayerProfileSettings.SectionName));

// ── Steam store enrichment backfill ──
builder.Services.Configure<GameEnrichmentSettings>(builder.Configuration.GetSection(GameEnrichmentSettings.SectionName));

// ── Steam achievements (schema + per-reviewer player achievements) ──
builder.Services.Configure<AchievementSettings>(builder.Configuration.GetSection(AchievementSettings.SectionName));
builder.Services.Configure<NewsSettings>(builder.Configuration.GetSection(NewsSettings.SectionName));

// ── SteamKit2 ──
// Registered in every role: an unstarted SteamKitService answers "not logged in" and
// does nothing, so the api role can still construct services that depend on it.
builder.Services.Configure<SteamSettings>(builder.Configuration.GetSection("SteamKit"));
builder.Services.AddSingleton<SteamKitService>();
builder.Services.AddSingleton<ISteamKitService>(sp => sp.GetRequiredService<SteamKitService>());

// ── Background services: the worker role (and today's single container) only ──
if (role.RunsHostedServices())
    builder.Services.AddWorkerHostedServices();

// ── Job-run tracking ──
builder.Services.AddSingleton<Cato.Infrastructure.Jobs.IJobRunTracker, Cato.Infrastructure.Jobs.JobRunTracker>();
builder.Services.AddSingleton(new Cato.API.Services.JobRuns.ApiProcessInfo(DateTime.UtcNow));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<Cato.API.Services.JobRuns.IJobCatalog, Cato.API.Services.JobRuns.JobCatalog>();
builder.Services.AddScoped<Cato.API.Services.JobRuns.JobRunStatusResolver>();

// ── Job dispatch (scheduled_job → job_dispatch → agents; the dispatcher loop runs in the worker) ──
builder.Services.Configure<Cato.API.Services.Dispatch.JobDispatcherSettings>(
    builder.Configuration.GetSection(Cato.API.Services.Dispatch.JobDispatcherSettings.SectionName));
builder.Services.AddScoped<Cato.API.Services.Dispatch.JobDispatchService>();

// ── Alerts (incidents, SNS e-mail, Discord; the monitor loop runs in the worker) ──
Cato.API.Services.Alerts.AlertServiceCollectionExtensions.AddAlerts(builder.Services, builder.Configuration);

// ── Application Services ──
builder.Services.AddScoped<ISteamGameEnrichmentService, SteamGameEnrichmentService>();
builder.Services.AddScoped<Cato.API.Services.IGameService, Cato.API.Services.GameService>();
builder.Services.AddScoped<Cato.API.Services.IGameDataService, Cato.API.Services.GameDataService>();
builder.Services.AddScoped<Cato.API.Services.IIngestionService, Cato.API.Services.IngestionService>();
builder.Services.AddScoped<Cato.API.Services.ISteamKitDataService, Cato.API.Services.SteamKitDataService>();
builder.Services.AddScoped<Cato.API.Services.IMarketingTargetService, Cato.API.Services.MarketingTargetService>();
builder.Services.AddScoped<Cato.API.Services.IMarketingActionService, Cato.API.Services.MarketingActionService>();

// ── CORS ──
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else
            policy.WithOrigins("https://your-prod-origin.com")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
    });
});

// ── Output cache (catalog feed for sibling services) ──
// Backed by the existing Redis so cached catalog responses are shared across
// instances, survive restarts, and repeated hits never touch Postgres.
builder.Services.AddStackExchangeRedisOutputCache(options =>
{
    var redis = builder.Configuration.GetSection("Redis").Get<RedisSettings>() ?? new RedisSettings();
    options.Configuration = redis.ConnectionString;
    options.InstanceName = $"{redis.InstanceName}-outputcache:";
});
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("GamesCatalog", policy => policy
        .Expire(TimeSpan.FromSeconds(builder.Configuration.GetValue("Catalog:CacheSeconds", 60)))
        .SetVaryByQuery("*")
        .Tag("games-catalog")); // evicted after enrichment so media shows immediately
});

// ── Swagger / OpenAPI ──
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "CATO API", Version = "v1" });
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

// ── Database migrations ──
// The migrate role applies them and exits before any hosted service starts; the deploy
// waits for its exit code. api and worker never migrate, so two tasks cannot race.
if (role == CatoRole.Migrate || role.MigratesOnStartup())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
    try
    {
        db.Database.Migrate();
    }
    catch (Exception ex) when (role == CatoRole.Migrate)
    {
        Log.Fatal(ex, "migrate_failed");
        await Log.CloseAndFlushAsync();
        return 1;
    }

    if (role == CatoRole.Migrate)
    {
        Log.Information("migrate_complete");
        await Log.CloseAndFlushAsync();
        return 0;
    }
}

// ── Middleware ──
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health probes arrive every few seconds; keep them out of the request log.
app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, exception) =>
    exception is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
    : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
    : LogEventLevel.Information);
app.UseCors("AllowFrontend");
app.UseOutputCache();
app.MapCatoHealthChecks();
app.MapControllers();

Log.Information("cato_started role={Role}", role);
await app.RunAsync();
return 0;
