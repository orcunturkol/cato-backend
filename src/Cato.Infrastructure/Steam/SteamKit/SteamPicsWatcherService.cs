using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Steam.Filtering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam.SteamKit;

/// <summary>
/// Background service that polls the Steam PICS change feed every N minutes,
/// discovers newly released games, and auto-saves them as Sourcing entries.
/// </summary>
public sealed class SteamPicsWatcherService : BackgroundService
{
    private readonly ISteamKitService _steamKit;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SteamPicsWatcherService> _logger;
    private readonly SteamSettings _settings;
    private readonly IGameQualityFilter _filter;
    private readonly IJobRunTracker _jobRunTracker;

    // File to persist the last seen PICS change number across restarts
    private readonly string _changeNumberFile;

    public SteamPicsWatcherService(
        ISteamKitService steamKit,
        IServiceScopeFactory scopeFactory,
        ILogger<SteamPicsWatcherService> logger,
        IOptions<SteamSettings> settings,
        IGameQualityFilter filter,
        IJobRunTracker jobRunTracker)
    {
        _steamKit = steamKit;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
        _filter = filter;
        _jobRunTracker = jobRunTracker;
        _changeNumberFile = Path.Combine(AppContext.BaseDirectory, "pics_change_number.txt");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for SteamKit2 to connect before starting polling
        _logger.LogInformation("SteamPicsWatcher: Waiting for SteamKit2 connection...");
        await WaitForConnectionAsync(stoppingToken);

        var interval = TimeSpan.FromMinutes(_settings.PicsPollingIntervalMinutes);
        _logger.LogInformation("SteamPicsWatcher: Starting PICS polling every {Minutes} minutes", _settings.PicsPollingIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollForNewGamesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SteamPicsWatcher: Error during PICS poll");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task WaitForConnectionAsync(CancellationToken ct)
    {
        while (!_steamKit.IsConnected && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private async Task PollForNewGamesAsync(CancellationToken ct)
    {
        await using var job = await _jobRunTracker.StartAsync("SteamPicsWatcher", ct: ct);
        try
        {
        var lastChangeNumber = LoadLastChangeNumber();

        var (changedAppIds, latestChangeNumber) = await _steamKit.GetChangedAppIdsSinceAsync(lastChangeNumber, ct);

        if (changedAppIds.Count == 0)
        {
            job.Set("changedAppIds", 0);
            job.Set("discovered", 0);
            _logger.LogDebug("SteamPicsWatcher: No changes since change #{ChangeNumber}", lastChangeNumber);
            SaveLastChangeNumber(latestChangeNumber);
            return;
        }

        _logger.LogInformation("SteamPicsWatcher: Processing {Count} changed AppIds", changedAppIds.Count);

        int discovered = 0;
        var newGameIds = new List<Guid>();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();

        foreach (var appId in changedAppIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Skip AppIds already tracked in the database
                var alreadyTracked = await db.Games.AnyAsync(g => g.AppId == (int)appId, ct);
                if (alreadyTracked) continue;

                var info = await _steamKit.GetAppInfoAsync(appId, ct);
                if (info is null) continue;

                // Allow "game" or "demo" types only
                if (!string.Equals(info.Type, "game", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(info.Type, "demo", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Must be released
                if (!string.Equals(info.ReleaseState, "released", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Must be released after Jan 1, 2023
                if (info.ReleaseDate is null || info.ReleaseDate < new DateOnly(2023, 1, 1))
                    continue;

                // Exclude free-to-play games
                if (info.IsFreeToPlay)
                    continue;

                // Pre-enrichment quality filter: reject obviously non-Latin-script names before spending a Steam Store API call
                if (_filter.ShouldRejectByName(info.Name))
                {
                    _logger.LogDebug("SteamPicsWatcher: Skipped AppId={AppId} Name='{Name}' — pre-enrichment filter (name:no-latin-letter)", appId, info.Name);
                    continue;
                }

                _logger.LogInformation("SteamPicsWatcher: Discovered new game AppId={AppId} Name='{Name}'", appId, info.Name);

                var gameId = Guid.NewGuid();
                db.Games.Add(new Game
                {
                    Id = gameId,
                    AppId = (int)appId,
                    Name = info.Name,
                    GameType = "Other",
                    IsReleased = true,
                    ReleaseDate = info.ReleaseDate,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                newGameIds.Add(gameId);

                discovered++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SteamPicsWatcher: Failed to process AppId {AppId}", appId);
            }
        }

        if (discovered > 0)
            await db.SaveChangesAsync(ct);

        // Enrich newly created games with full Steam Store data
        if (newGameIds.Count > 0)
        {
            var enrichment = scope.ServiceProvider.GetRequiredService<ISteamGameEnrichmentService>();
            int enriched = 0;

            foreach (var gameId in newGameIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var success = await enrichment.EnrichGameAsync(gameId, ct);
                    if (success)
                    {
                        enriched++;
                        await ApplyPostEnrichmentFilterAsync(db, gameId, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SteamPicsWatcher: Failed to enrich game {GameId}", gameId);
                }
            }

            _logger.LogInformation("SteamPicsWatcher: Enriched {Enriched}/{Total} newly discovered games",
                enriched, newGameIds.Count);
        }

        SaveLastChangeNumber(latestChangeNumber);

        job.Set("changedAppIds", changedAppIds.Count);
        job.Set("discovered", discovered);

        _logger.LogInformation("SteamPicsWatcher: Poll complete — {Discovered} new games discovered (change #{ChangeNumber})",
            discovered, latestChangeNumber);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }

    private async Task ApplyPostEnrichmentFilterAsync(CatoDbContext db, Guid gameId, CancellationToken ct)
    {
        var game = await db.Games
            .Include(g => g.Tags)
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null) return;

        var decision = _filter.Evaluate(game, FilterStage.PostEnrichment);
        if (!decision.Rejected) return;

        var appId = game.AppId;
        var name = game.Name;

        db.Games.Remove(game);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SteamPicsWatcher: Deleted AppId={AppId} Name='{Name}' — post-enrichment filter ({Reason})",
            appId, name, decision.Reason);
    }

    private uint LoadLastChangeNumber()
    {
        if (File.Exists(_changeNumberFile) &&
            uint.TryParse(File.ReadAllText(_changeNumberFile).Trim(), out var n))
            return n;

        // If no file exists, start from a sentinel that fetches only very recent changes
        return 0;
    }

    private void SaveLastChangeNumber(uint changeNumber)
    {
        if (changeNumber == 0) return;
        File.WriteAllText(_changeNumberFile, changeNumber.ToString());
    }
}
