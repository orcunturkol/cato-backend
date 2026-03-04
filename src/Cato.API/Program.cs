using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.SteamKit;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Serilog;

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

// ── RabbitMQ ──
builder.Services.Configure<Cato.Infrastructure.Messaging.RabbitMqSettings>(
    builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddScoped<Cato.Infrastructure.Messaging.IIngestionDispatcher, Cato.API.Services.IngestionDispatcher>();
builder.Services.AddHostedService<Cato.Infrastructure.Messaging.RabbitMqConsumerService>();

// ── SteamKit2 ──
builder.Services.Configure<SteamSettings>(builder.Configuration.GetSection("SteamKit"));
builder.Services.AddSingleton<SteamKitService>();
builder.Services.AddSingleton<ISteamKitService>(sp => sp.GetRequiredService<SteamKitService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SteamKitService>());
builder.Services.AddHostedService<SteamPicsWatcherService>();

// ── Application Services ──
builder.Services.AddScoped<Cato.API.Services.IGameService, Cato.API.Services.GameService>();
builder.Services.AddScoped<Cato.API.Services.IGameDataService, Cato.API.Services.GameDataService>();
builder.Services.AddScoped<Cato.API.Services.IIngestionService, Cato.API.Services.IngestionService>();
builder.Services.AddScoped<Cato.API.Services.ISteamKitDataService, Cato.API.Services.SteamKitDataService>();

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
app.MapControllers();

app.Run();
