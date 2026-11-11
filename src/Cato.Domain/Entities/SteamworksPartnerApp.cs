namespace Cato.Domain.Entities;

/// <summary>
/// An app that a Steamworks partner lists on its partner.steampowered.com wishlist pages, so
/// the partner's "Lifetime Wishlist Summary" is the sum over its apps. The collector replaces a
/// partner's set after each listing.
/// </summary>
public class SteamworksPartnerApp
{
    public int PartnerId { get; set; }
    public int AppId { get; set; }
    public DateTime LastSeenAt { get; set; }
}
