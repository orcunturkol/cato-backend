namespace Cato.Domain.Entities;

/// <summary>
/// A Steamworks partner (organization) that the collector's Steamworks login can see, from
/// the "View as" list of partner.steampowered.com. The collector fetches the enabled ones;
/// a partner's financial data needs its own Financial API key in the collector's config.
/// </summary>
public class SteamworksPartner
{
    /// <summary>PLAN B YAZILIM, the first partner: the owner of every row stored before partners were tracked.</summary>
    public const int LegacyPartnerId = 216530;

    public int PartnerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTime DiscoveredAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}
