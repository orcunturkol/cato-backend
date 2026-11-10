using Cato.API.Models.SteamworksPartners;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.SteamworksPartners;

/// <summary>Switches the collector's fetch of one partner on or off.</summary>
public class UpdateSteamworksPartnerHandler : IRequestHandler<UpdateSteamworksPartnerCommand, SteamworksPartnerDto?>
{
    private readonly CatoDbContext _db;

    public UpdateSteamworksPartnerHandler(CatoDbContext db) => _db = db;

    public async Task<SteamworksPartnerDto?> Handle(UpdateSteamworksPartnerCommand command, CancellationToken ct)
    {
        var partner = await _db.SteamworksPartners.FirstOrDefaultAsync(p => p.PartnerId == command.PartnerId, ct);
        if (partner is null) return null;

        partner.Enabled = command.Enabled;
        await _db.SaveChangesAsync(ct);
        return SteamworksPartnerDto.Of(partner);
    }
}
