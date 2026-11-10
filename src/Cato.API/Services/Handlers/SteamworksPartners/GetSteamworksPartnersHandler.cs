using Cato.API.Models.SteamworksPartners;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.SteamworksPartners;

public class GetSteamworksPartnersHandler : IRequestHandler<GetSteamworksPartnersQuery, List<SteamworksPartnerDto>>
{
    private readonly CatoDbContext _db;

    public GetSteamworksPartnersHandler(CatoDbContext db) => _db = db;

    public async Task<List<SteamworksPartnerDto>> Handle(GetSteamworksPartnersQuery request, CancellationToken ct)
    {
        var partners = await _db.SteamworksPartners.AsNoTracking().OrderBy(p => p.PartnerId).ToListAsync(ct);
        return partners.Select(SteamworksPartnerDto.Of).ToList();
    }
}
