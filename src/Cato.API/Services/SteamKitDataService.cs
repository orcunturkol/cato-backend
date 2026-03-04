using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services;

public class SteamKitDataService : ISteamKitDataService
{
    private readonly ISteamKitService _steamKit;
    private readonly CatoDbContext _db;
    private readonly ILogger<SteamKitDataService> _logger;

    public SteamKitDataService(ISteamKitService steamKit, CatoDbContext db, ILogger<SteamKitDataService> logger)
    {
        _steamKit = steamKit;
        _db = db;
        _logger = logger;
    }

    public Task<int?> GetLiveCcuAsync(int appId, CancellationToken ct = default)
        => _steamKit.GetCurrentPlayerCountAsync(appId, ct);

    public async Task<int?> SaveLiveCcuAsync(int appId, CancellationToken ct = default)
    {
        var ccu = await _steamKit.GetCurrentPlayerCountAsync(appId, ct);
        if (ccu is null)
        {
            _logger.LogWarning("SteamKitDataService: Could not fetch CCU for AppId {AppId}", appId);
            return null;
        }

        var game = await _db.Games.FirstOrDefaultAsync(g => g.AppId == appId, ct);
        if (game is null)
        {
            _logger.LogWarning("SteamKitDataService: AppId {AppId} not found in database. CCU not saved.", appId);
            return ccu;
        }

        var timestamp = DateTime.UtcNow;

        // Avoid duplicate entries within the same minute
        var exists = await _db.CcuHistories.AnyAsync(
            c => c.GameId == game.Id && c.Source == "SteamKit2" &&
                 c.Timestamp >= timestamp.AddMinutes(-1), ct);

        if (!exists)
        {
            _db.CcuHistories.Add(new CcuHistory
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                Timestamp = timestamp,
                CcuCount = ccu.Value,
                Source = "SteamKit2"
            });

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("SteamKitDataService: Saved CCU {Ccu} for AppId {AppId}", ccu, appId);
        }

        return ccu;
    }
}
