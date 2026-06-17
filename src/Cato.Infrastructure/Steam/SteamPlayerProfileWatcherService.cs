using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// Rotates through reviewer SteamIDs (Redis sorted set seeded by
/// SteamReviewWatcherService) and enriches them via GetPlayerSummaries,
/// upserting into steam_player_profile. Latest-only: re-fetches overwrite.
/// Ids Steam omits from a successful response (deleted/invalid accounts) are
/// counted as failures and eventually quarantined.
/// </summary>
public class SteamPlayerProfileWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SteamPlayerProfileWatcherService> _logger;
    private readonly PlayerProfileSettings _settings;
    private readonly SteamWebApiSettings _webApiSettings;

    public SteamPlayerProfileWatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<SteamPlayerProfileWatcherService> logger,
        IOptions<PlayerProfileSettings> settings,
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
            _logger.LogInformation("Player profile watcher disabled via PlayerProfile:Enabled");
            return;
        }

        if (string.IsNullOrWhiteSpace(_webApiSettings.ApiKey))
        {
            _logger.LogWarning(
                "Player profile watcher idle — no Steam Web API key configured (SteamWebApi:ApiKey / env SteamWebApi__ApiKey)");
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
                _logger.LogError(ex, "Player profile fetch cycle failed");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(_settings.IntervalMinutes),
                stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var steamApi = scope.ServiceProvider.GetRequiredService<ISteamApiService>();
        var rotation = scope.ServiceProvider.GetRequiredService<ISteamIdRotationService>();

        var ids = await rotation.FetchBatchAsync(_settings.BatchSize, ct);
        if (ids.Count == 0)
        {
            _logger.LogInformation("Player profile cycle: rotation is empty, nothing to fetch");
            return;
        }

        var fetched = 0;
        var added = 0;
        var updated = 0;
        var failed = 0;
        var quarantined = 0;
        var skippedChunks = 0;

        foreach (var chunk in ids.Chunk(100))
        {
            if (ct.IsCancellationRequested) break;

            var response = await steamApi.GetPlayerSummariesAsync(chunk, ct);
            if (response is null)
            {
                // Transient API/network failure — mark nothing, so an outage
                // can't mass-quarantine valid ids. Retried next cycle.
                skippedChunks++;
                continue;
            }

            var returned = new Dictionary<long, SteamPlayerSummary>();
            foreach (var player in response.Response.Players)
            {
                if (long.TryParse(player.SteamId, out var sid))
                    returned[sid] = player;
            }

            var now = DateTime.UtcNow;
            var (chunkAdded, chunkUpdated) = await UpsertProfilesAsync(db, returned, now, ct);
            added += chunkAdded;
            updated += chunkUpdated;
            fetched += returned.Count;

            // Mark Redis only after the DB save: if the save throws, scores
            // stay stale and the chunk is retried next cycle (upsert is idempotent).
            foreach (var id in chunk)
            {
                if (returned.ContainsKey(id))
                {
                    await rotation.MarkSuccessAsync(id, now, ct);
                }
                else
                {
                    failed++;
                    if (await rotation.MarkFailureAsync(id, "omitted_from_player_summaries", ct))
                        quarantined++;
                }
            }
        }

        _logger.LogInformation(
            "Player profile cycle complete: requested={Requested} fetched={Fetched} added={Added} updated={Updated} failed={Failed} quarantined={Quarantined} skippedChunks={SkippedChunks}",
            ids.Count, fetched, added, updated, failed, quarantined, skippedChunks);
    }

    private static async Task<(int Added, int Updated)> UpsertProfilesAsync(
        CatoDbContext db,
        Dictionary<long, SteamPlayerSummary> players,
        DateTime now,
        CancellationToken ct)
    {
        if (players.Count == 0) return (0, 0);

        var ids = players.Keys.ToList();
        var existing = await db.SteamPlayerProfiles
            .Where(p => ids.Contains(p.SteamId64))
            .ToDictionaryAsync(p => p.SteamId64, ct);

        var added = 0;
        var updated = 0;

        foreach (var (steamId, player) in players)
        {
            SteamPlayerProfile entity;
            if (existing.TryGetValue(steamId, out var found))
            {
                entity = found;
                updated++;
            }
            else
            {
                entity = db.SteamPlayerProfiles
                    .Add(new SteamPlayerProfile { Id = Guid.NewGuid(), SteamId64 = steamId })
                    .Entity;
                added++;
            }

            entity.CommunityVisibilityState = player.CommunityVisibilityState;
            entity.ProfileState = player.ProfileState;
            entity.PersonaName = player.PersonaName ?? string.Empty;
            entity.ProfileUrl = player.ProfileUrl;
            entity.AvatarHash = player.AvatarHash;
            entity.AvatarFullUrl = player.AvatarFull;
            entity.PersonaState = player.PersonaState;
            entity.PersonaStateFlags = player.PersonaStateFlags;
            entity.RealName = player.RealName;
            entity.PrimaryClanId = player.PrimaryClanId;
            entity.TimeCreated = player.TimeCreated is long tc
                ? DateTimeOffset.FromUnixTimeSeconds(tc).UtcDateTime : null;
            entity.LastLogoff = player.LastLogoff is long ll
                ? DateTimeOffset.FromUnixTimeSeconds(ll).UtcDateTime : null;
            entity.LocCountryCode = player.LocCountryCode;
            entity.LocStateCode = player.LocStateCode;
            entity.LocCityId = player.LocCityId;
            entity.LastFetchedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return (added, updated);
    }
}
