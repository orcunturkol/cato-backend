using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
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

    // File to persist the last seen PICS change number across restarts
    private readonly string _changeNumberFile;

    public SteamPicsWatcherService(
        ISteamKitService steamKit,
        IServiceScopeFactory scopeFactory,
        ILogger<SteamPicsWatcherService> logger,
        IOptions<SteamSettings> settings)
    {
        _steamKit = steamKit;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
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
        var lastChangeNumber = LoadLastChangeNumber();

        var (changedAppIds, latestChangeNumber) = await _steamKit.GetChangedAppIdsSinceAsync(lastChangeNumber, ct);

        if (changedAppIds.Count == 0)
        {
            _logger.LogDebug("SteamPicsWatcher: No changes since change #{ChangeNumber}", lastChangeNumber);
            SaveLastChangeNumber(latestChangeNumber);
            return;
        }

        _logger.LogInformation("SteamPicsWatcher: Processing {Count} changed AppIds", changedAppIds.Count);

        int discovered = 0;
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

                // Only auto-save released games (filter out DLCs, tools, demos, etc.)
                if (!string.Equals(info.Type, "game", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(info.ReleaseState, "released", StringComparison.OrdinalIgnoreCase)) continue;

                _logger.LogInformation("SteamPicsWatcher: Discovered new game AppId={AppId} Name='{Name}'", appId, info.Name);

                db.Games.Add(new Game
                {
                    Id = Guid.NewGuid(),
                    AppId = (int)appId,
                    Name = info.Name,
                    GameType = "Sourcing",
                    IsReleased = true,
                    ReleaseDate = info.ReleaseDate,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                discovered++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SteamPicsWatcher: Failed to process AppId {AppId}", appId);
            }
        }

        if (discovered > 0)
            await db.SaveChangesAsync(ct);

        SaveLastChangeNumber(latestChangeNumber);

        _logger.LogInformation("SteamPicsWatcher: Poll complete — {Discovered} new games discovered (change #{ChangeNumber})",
            discovered, latestChangeNumber);
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
