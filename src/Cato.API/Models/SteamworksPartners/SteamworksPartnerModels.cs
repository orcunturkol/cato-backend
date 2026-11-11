using Cato.Domain.Entities;
using MediatR;

namespace Cato.API.Models.SteamworksPartners;

/// <summary>A Steamworks partner the collector fetches when <see cref="Enabled"/>.</summary>
public record SteamworksPartnerDto(int PartnerId, string Name, bool Enabled, DateTime DiscoveredAt, DateTime? LastSeenAt)
{
    public static SteamworksPartnerDto Of(SteamworksPartner p) =>
        new(p.PartnerId, p.Name, p.Enabled, p.DiscoveredAt, p.LastSeenAt);
}

public record GetSteamworksPartnersQuery : IRequest<List<SteamworksPartnerDto>>;

/// <summary>One option of the "View as" list on partner.steampowered.com.</summary>
public record DiscoveredPartner(int PartnerId, string Name);

/// <summary>Body of POST /api/steamworks-partners/discovered.</summary>
public record ReportDiscoveredPartnersRequest(List<DiscoveredPartner>? Partners);

public record ReportDiscoveredPartnersCommand(List<DiscoveredPartner> Partners) : IRequest<List<SteamworksPartnerDto>>;

/// <summary>Body of PUT /api/steamworks-partners/{partnerId}.</summary>
public record UpdateSteamworksPartnerRequest(bool? Enabled);

/// <summary>Null when the partner is unknown.</summary>
public record UpdateSteamworksPartnerCommand(int PartnerId, bool Enabled) : IRequest<SteamworksPartnerDto?>;

/// <summary>Body of PUT /api/steamworks-partners/{partnerId}/apps.</summary>
public record ReplaceSteamworksPartnerAppsRequest(List<int>? AppIds);

/// <summary>Null when the partner is unknown; else the stored app ids, sorted.</summary>
public record ReplaceSteamworksPartnerAppsCommand(int PartnerId, List<int> AppIds) : IRequest<List<int>?>;
