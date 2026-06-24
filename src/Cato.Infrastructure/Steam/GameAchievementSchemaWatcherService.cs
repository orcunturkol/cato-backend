using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Steam.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// Refreshes the per-game achievement catalog (GetSchemaForGame) for tracked
/// (Sourcing/Owned) games into game_achievement_schema. This is the denominator
/// for the "X of Y achievements at review" metric. Latest-only upsert per
/// (GameId, ApiName); games with no achievements are valid (recorded as zero).
/// </summary>
public class GameAchievementSchemaWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GameAchievementSchemaWatcherService> _logger;
    private readonly AchievementSettings _settings;
    private readonly SteamWebApiSettings _webApiSettings;

    public GameAchievementSchemaWatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<GameAchievementSchemaWatcherService> logger,
        IOptions<AchievementSettings> settings,
        IOptions<SteamWebApiSettings> webApiSettings)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
        _webApiSettings = webApiSettings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Achievement schema watcher disabled via Achievements:Enabled");
            return;
        }

        if (string.IsNullOrWhiteSpace(_webApiSettings.ApiKey))
        {
            _logger.LogWarning(
                "Achievement schema watcher idle — no Steam Web API key configured (SteamWebApi:ApiKey / env SteamWebApi__ApiKey)");
            return;
        }

        // Brief startup delay to let the database finish migrating.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Achievement schema fetch cycle failed");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(_settings.SchemaIntervalMinutes),
                stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var steamApi = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
        var tracker = scope.ServiceProvider.GetRequiredService<IJobRunTracker>();

        await using var job = await tracker.StartAsync("GameAchievementSchemaWatcher", ct: ct);
        try
        {
        var games = await db.Games
            .Where(g => g.GameType == "Sourcing" || g.GameType == "Owned")
            .Select(g => new { g.Id, g.AppId })
            .ToListAsync(ct);

        var withAchievements = 0;
        var zeroAchievement = 0;
        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var game in games)
        {
            if (ct.IsCancellationRequested) break;

            var response = await steamApi.GetSchemaForGameAsync(game.AppId, ct);
            if (response is null)
            {
                // Transient API/network failure — leave any existing schema intact.
                skipped++;
                continue;
            }

            var achievements = response.Game.AvailableGameStats?.Achievements ?? [];
            if (achievements.Count == 0)
            {
                zeroAchievement++;
                continue;
            }

            withAchievements++;
            var (gameAdded, gameUpdated) = await UpsertSchemaAsync(db, game.Id, game.AppId, achievements, ct);
            added += gameAdded;
            updated += gameUpdated;
        }

        job.Set("games", games.Count);
        job.Set("withAchievements", withAchievements);
        job.Set("zeroAchievement", zeroAchievement);
        job.Set("added", added);
        job.Set("updated", updated);
        job.Set("skipped", skipped);

        _logger.LogInformation(
            "Achievement schema cycle complete: games={Games} withAchievements={WithAchievements} zeroAchievement={ZeroAchievement} added={Added} updated={Updated} skipped={Skipped}",
            games.Count, withAchievements, zeroAchievement, added, updated, skipped);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }

    private static async Task<(int Added, int Updated)> UpsertSchemaAsync(
        CatoDbContext db,
        Guid gameId,
        int appId,
        List<SteamSchemaAchievement> achievements,
        CancellationToken ct)
    {
        var existing = await db.GameAchievementSchemas
            .Where(a => a.GameId == gameId)
            .ToDictionaryAsync(a => a.ApiName, ct);

        var added = 0;
        var updated = 0;

        foreach (var ach in achievements)
        {
            if (string.IsNullOrEmpty(ach.Name)) continue;

            GameAchievementSchema entity;
            if (existing.TryGetValue(ach.Name, out var found))
            {
                entity = found;
                updated++;
            }
            else
            {
                entity = db.GameAchievementSchemas
                    .Add(new GameAchievementSchema { Id = Guid.NewGuid(), GameId = gameId, ApiName = ach.Name })
                    .Entity;
                added++;
            }

            entity.AppId = appId;
            entity.DisplayName = ach.DisplayName;
            entity.Description = ach.Description;
            entity.Hidden = ach.Hidden != 0;
            entity.IconUrl = ach.Icon;
            entity.IconGrayUrl = ach.IconGray;
        }

        await db.SaveChangesAsync(ct);
        return (added, updated);
    }
}
