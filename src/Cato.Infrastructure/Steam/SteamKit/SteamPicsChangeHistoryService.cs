using System.Text.Json;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam.SteamKit;

/// <summary>
/// Background service that tracks PICS change history for games in the database.
/// On each poll, fetches the full KeyValue tree for changed tracked apps,
/// diffs against the previous snapshot, and stores granular change records.
/// </summary>
public sealed class SteamPicsChangeHistoryService : BackgroundService
{
    private readonly ISteamKitService _steamKit;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SteamPicsChangeHistoryService> _logger;
    private readonly SteamSettings _settings;
    private readonly IJobRunTracker _jobRunTracker;

    private readonly string _changeNumberFile;

    public SteamPicsChangeHistoryService(
        ISteamKitService steamKit,
        IServiceScopeFactory scopeFactory,
        ILogger<SteamPicsChangeHistoryService> logger,
        IOptions<SteamSettings> settings,
        IJobRunTracker jobRunTracker)
    {
        _steamKit = steamKit;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings.Value;
        _jobRunTracker = jobRunTracker;
        _changeNumberFile = Path.Combine(AppContext.BaseDirectory, "pics_history_change_number.txt");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PicsChangeHistory: Waiting for SteamKit2 connection...");
        await WaitForConnectionAsync(stoppingToken);

        var interval = TimeSpan.FromMinutes(_settings.PicsPollingIntervalMinutes);
        _logger.LogInformation("PicsChangeHistory: Starting change history polling every {Minutes} minutes",
            _settings.PicsPollingIntervalMinutes);

        // Bootstrap: take initial snapshots for tracked games that have none
        await BootstrapSnapshotsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollForChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PicsChangeHistory: Error during change history poll");
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

    /// <summary>
    /// On first run, take initial snapshots for all tracked games that don't have one yet.
    /// </summary>
    private async Task BootstrapSnapshotsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();

        // Find tracked games with no snapshot
        var gamesWithSnapshots = await db.AppKeyValueSnapshots
            .Select(s => s.AppId)
            .Distinct()
            .ToListAsync(ct);

        var trackedGames = await db.Games
            .Where(g => g.IsReleased)
            .Where(g => g.ReleaseDate != null && g.ReleaseDate >= new DateOnly(2023, 1, 1))
            .Where(g => g.PriceUsd != null && g.PriceUsd > 0)
            .Select(g => new { g.AppId, g.Id })
            .ToListAsync(ct);

        var gamesNeedingSnapshot = trackedGames
            .Where(g => !gamesWithSnapshots.Contains(g.AppId))
            .ToList();

        if (gamesNeedingSnapshot.Count == 0)
        {
            _logger.LogInformation("PicsChangeHistory: All tracked games already have initial snapshots");
            return;
        }

        _logger.LogInformation("PicsChangeHistory: Bootstrapping initial snapshots for {Count} tracked games",
            gamesNeedingSnapshot.Count);

        var appIds = gamesNeedingSnapshot.Select(g => (uint)g.AppId).ToList();
        var rawInfos = await _steamKit.GetRawAppInfoBatchAsync(appIds, ct);
        var gameIdByAppId = gamesNeedingSnapshot.ToDictionary(g => g.AppId, g => g.Id);
        var now = DateTime.UtcNow;

        foreach (var raw in rawInfos)
        {
            var appId = (int)raw.AppId;
            gameIdByAppId.TryGetValue(appId, out var gameId);

            db.AppKeyValueSnapshots.Add(new AppKeyValueSnapshot
            {
                Id = Guid.NewGuid(),
                AppId = appId,
                GameId = gameId,
                ChangeNumber = raw.ChangeNumber,
                RawKeyValuesJson = JsonSerializer.Serialize(raw.KeyValues),
                CapturedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("PicsChangeHistory: Bootstrapped {Count} initial snapshots", rawInfos.Count);
    }

    private async Task PollForChangesAsync(CancellationToken ct)
    {
        await using var job = await _jobRunTracker.StartAsync("SteamPicsChangeHistory", ct: ct);
        try
        {
        var lastChangeNumber = LoadLastChangeNumber();
        var (changedAppIds, latestChangeNumber) = await _steamKit.GetChangedAppIdsSinceAsync(lastChangeNumber, ct);

        if (changedAppIds.Count == 0)
        {
            job.Set("changedAppIds", 0);
            job.Set("relevantAppIds", 0);
            job.Set("changeRecords", 0);
            _logger.LogDebug("PicsChangeHistory: No changes since change #{ChangeNumber}", lastChangeNumber);
            SaveLastChangeNumber(latestChangeNumber);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();

        // Intersect with tracked games (same filters as discovery: released, paid, post-2023)
        var trackedGames = await db.Games
            .Where(g => g.IsReleased)
            .Where(g => g.ReleaseDate != null && g.ReleaseDate >= new DateOnly(2023, 1, 1))
            .Where(g => g.PriceUsd != null && g.PriceUsd > 0)
            .Select(g => new { g.AppId, g.Id })
            .ToListAsync(ct);

        var gameIdByAppId = trackedGames.ToDictionary(g => g.AppId, g => g.Id);
        var trackedAppIds = new HashSet<int>(trackedGames.Select(g => g.AppId));

        var relevantAppIds = changedAppIds
            .Where(id => trackedAppIds.Contains((int)id))
            .ToList();

        if (relevantAppIds.Count == 0)
        {
            job.Set("changedAppIds", changedAppIds.Count);
            job.Set("relevantAppIds", 0);
            job.Set("changeRecords", 0);
            _logger.LogDebug("PicsChangeHistory: {Total} apps changed but none are tracked", changedAppIds.Count);
            SaveLastChangeNumber(latestChangeNumber);
            return;
        }

        _logger.LogInformation("PicsChangeHistory: Processing {Relevant}/{Total} changed apps that are tracked",
            relevantAppIds.Count, changedAppIds.Count);

        // Batch fetch raw app info
        var rawInfos = await _steamKit.GetRawAppInfoBatchAsync(relevantAppIds, ct);
        var now = DateTime.UtcNow;
        int totalChanges = 0;

        foreach (var raw in rawInfos)
        {
            try
            {
                var appId = (int)raw.AppId;
                gameIdByAppId.TryGetValue(appId, out var gameId);

                // Load previous snapshot
                var previousSnapshot = await db.AppKeyValueSnapshots
                    .Where(s => s.AppId == appId)
                    .OrderByDescending(s => s.ChangeNumber)
                    .FirstOrDefaultAsync(ct);

                var newFlat = KeyValueDiffUtility.Flatten(raw.KeyValues);

                if (previousSnapshot is not null)
                {
                    // Skip if same change number
                    if (previousSnapshot.ChangeNumber == raw.ChangeNumber)
                        continue;

                    var oldDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(previousSnapshot.RawKeyValuesJson);
                    if (oldDict is not null)
                    {
                        var oldFlat = KeyValueDiffUtility.Flatten(NormalizeDeserialized(oldDict));
                        var changes = KeyValueDiffUtility.Diff(oldFlat, newFlat);

                        foreach (var change in changes)
                        {
                            db.AppChangeRecords.Add(new AppChangeRecord
                            {
                                Id = Guid.NewGuid(),
                                AppId = appId,
                                GameId = gameId,
                                ChangeNumber = raw.ChangeNumber,
                                Section = change.Section,
                                KeyPath = change.KeyPath,
                                Action = change.Action,
                                OldValue = change.OldValue,
                                NewValue = change.NewValue,
                                DetectedAt = now,
                            });
                        }

                        totalChanges += changes.Count;

                        if (changes.Count > 0)
                        {
                            _logger.LogInformation(
                                "PicsChangeHistory: AppId {AppId} — {Count} changes in changelist #{ChangeNumber}",
                                appId, changes.Count, raw.ChangeNumber);
                        }
                    }
                }
                else
                {
                    // First snapshot for this app — generate "Added" records
                    foreach (var (key, value) in newFlat)
                    {
                        var dotIdx = key.IndexOf('.');
                        var section = dotIdx >= 0 ? key[..dotIdx] : key;

                        db.AppChangeRecords.Add(new AppChangeRecord
                        {
                            Id = Guid.NewGuid(),
                            AppId = appId,
                            GameId = gameId,
                            ChangeNumber = raw.ChangeNumber,
                            Section = section,
                            KeyPath = key,
                            Action = "Added",
                            OldValue = null,
                            NewValue = value,
                            DetectedAt = now,
                        });
                    }
                }

                // Store new snapshot
                db.AppKeyValueSnapshots.Add(new AppKeyValueSnapshot
                {
                    Id = Guid.NewGuid(),
                    AppId = appId,
                    GameId = gameId,
                    ChangeNumber = raw.ChangeNumber,
                    RawKeyValuesJson = JsonSerializer.Serialize(raw.KeyValues),
                    CapturedAt = now,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PicsChangeHistory: Failed to process AppId {AppId}", raw.AppId);
            }
        }

        await db.SaveChangesAsync(ct);
        SaveLastChangeNumber(latestChangeNumber);

        job.Set("changedAppIds", changedAppIds.Count);
        job.Set("relevantAppIds", relevantAppIds.Count);
        job.Set("appsProcessed", rawInfos.Count);
        job.Set("changeRecords", totalChanges);

        _logger.LogInformation(
            "PicsChangeHistory: Poll complete — {Changes} change records from {Apps} apps (change #{ChangeNumber})",
            totalChanges, rawInfos.Count, latestChangeNumber);
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message);
            throw;
        }
    }

    /// <summary>
    /// When deserializing JSON back to Dictionary, JsonElements need to be converted
    /// back to the nested Dictionary/string structure expected by the Flatten utility.
    /// </summary>
    private static Dictionary<string, object?> NormalizeDeserialized(Dictionary<string, object?> dict)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, value) in dict)
        {
            if (value is JsonElement element)
            {
                result[key] = NormalizeJsonElement(element);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => NormalizeDeserialized(
                element.EnumerateObject()
                    .ToDictionary(p => p.Name, p => (object?)p.Value, StringComparer.Ordinal)),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    private uint LoadLastChangeNumber()
    {
        if (File.Exists(_changeNumberFile) &&
            uint.TryParse(File.ReadAllText(_changeNumberFile).Trim(), out var n))
            return n;
        return 0;
    }

    private void SaveLastChangeNumber(uint changeNumber)
    {
        if (changeNumber == 0) return;
        File.WriteAllText(_changeNumberFile, changeNumber.ToString());
    }
}
