using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.Filtering;
using Cato.Infrastructure.Steam.SteamKit;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
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
builder.Services.AddControllers();

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
builder.Services.AddHostedService<RedisBackfillHostedService>();

// ── RabbitMQ ──
builder.Services.Configure<Cato.Infrastructure.Messaging.RabbitMqSettings>(
    builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddScoped<Cato.Infrastructure.Messaging.IIngestionDispatcher, Cato.API.Services.IngestionDispatcher>();
builder.Services.AddScoped<Cato.Infrastructure.Messaging.IBatchIngestionDispatcher, Cato.API.Services.BatchIngestionDispatcher>();
builder.Services.AddHostedService<Cato.Infrastructure.Messaging.RabbitMqConsumerService>();

// ── Game quality filter ──
builder.Services.Configure<GameFilterOptions>(builder.Configuration.GetSection(GameFilterOptions.SectionName));
builder.Services.AddSingleton<IGameQualityFilter, GameQualityFilterService>();

// ── Steam Web API + player profile watcher ──
builder.Services.Configure<SteamWebApiSettings>(builder.Configuration.GetSection(SteamWebApiSettings.SectionName));
builder.Services.Configure<PlayerProfileSettings>(builder.Configuration.GetSection(PlayerProfileSettings.SectionName));
builder.Services.AddHostedService<SteamPlayerProfileWatcherService>();

// ── SteamKit2 ──
builder.Services.Configure<SteamSettings>(builder.Configuration.GetSection("SteamKit"));
builder.Services.AddSingleton<SteamKitService>();
builder.Services.AddSingleton<ISteamKitService>(sp => sp.GetRequiredService<SteamKitService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SteamKitService>());
builder.Services.AddHostedService<SteamPicsWatcherService>();
builder.Services.AddHostedService<SteamPriceWatcherService>();
builder.Services.AddHostedService<SteamPicsChangeHistoryService>();
builder.Services.AddHostedService<SteamReviewWatcherService>();

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

// ── Auto-migrate database on startup ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
    db.Database.Migrate();
}

// ── Middleware ──
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors("AllowFrontend");
app.MapControllers();

app.Run();
