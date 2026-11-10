using Cato.API.Models.SteamworksPartners;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.SteamworksPartners;

/// <summary>
/// Stores the partners the collector's Steamworks login sees. A new partner is enabled, so the
/// collector fetches it from its next run; a known one gets its current name and LastSeenAt.
/// A disabled partner stays disabled. Partners missing from the list are kept.
/// </summary>
public class ReportDiscoveredPartnersHandler : IRequestHandler<ReportDiscoveredPartnersCommand, List<SteamworksPartnerDto>>
{
    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public ReportDiscoveredPartnersHandler(CatoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<List<SteamworksPartnerDto>> Handle(ReportDiscoveredPartnersCommand command, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var known = await _db.SteamworksPartners.ToDictionaryAsync(p => p.PartnerId, ct);

        foreach (var seen in command.Partners)
        {
            var name = seen.Name.Trim();
            if (name.Length > 200) name = name[..200];

            if (known.TryGetValue(seen.PartnerId, out var partner))
            {
                partner.Name = name;
                partner.LastSeenAt = now;
                continue;
            }

            partner = new SteamworksPartner
            {
                PartnerId = seen.PartnerId,
                Name = name,
                Enabled = true,
                DiscoveredAt = now,
                LastSeenAt = now,
            };
            _db.SteamworksPartners.Add(partner);
            known[seen.PartnerId] = partner;
        }
        await _db.SaveChangesAsync(ct);

        return known.Values.OrderBy(p => p.PartnerId).Select(SteamworksPartnerDto.Of).ToList();
    }
}
