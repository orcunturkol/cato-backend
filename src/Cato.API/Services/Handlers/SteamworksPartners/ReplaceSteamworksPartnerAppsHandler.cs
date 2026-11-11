using Cato.API.Models.SteamworksPartners;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.SteamworksPartners;

/// <summary>
/// Replaces the apps one partner lists: deletes the apps missing from the list, adds the new
/// ones and sets LastSeenAt on all of them. An empty list clears the partner's apps.
/// </summary>
public class ReplaceSteamworksPartnerAppsHandler : IRequestHandler<ReplaceSteamworksPartnerAppsCommand, List<int>?>
{
    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public ReplaceSteamworksPartnerAppsHandler(CatoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<List<int>?> Handle(ReplaceSteamworksPartnerAppsCommand command, CancellationToken ct)
    {
        if (!await _db.SteamworksPartners.AnyAsync(p => p.PartnerId == command.PartnerId, ct))
            return null;

        var now = _time.GetUtcNow().UtcDateTime;
        var wanted = command.AppIds.ToHashSet();
        var stored = await _db.SteamworksPartnerApps.Where(a => a.PartnerId == command.PartnerId).ToListAsync(ct);

        foreach (var app in stored)
        {
            if (wanted.Remove(app.AppId))
                app.LastSeenAt = now;
            else
                _db.SteamworksPartnerApps.Remove(app);
        }
        foreach (var appId in wanted)
            _db.SteamworksPartnerApps.Add(new SteamworksPartnerApp { PartnerId = command.PartnerId, AppId = appId, LastSeenAt = now });
        await _db.SaveChangesAsync(ct);

        return command.AppIds.Distinct().Order().ToList();
    }
}
