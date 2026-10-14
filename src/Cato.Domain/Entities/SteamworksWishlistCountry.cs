namespace Cato.Domain.Entities;

/// <summary>
/// One country on one day of the Steamworks wishlist-by-country report (partner
/// CSV <c>QueryWishlistActionsByCountryForCSV</c>) for an owned game.
///
/// <c>Country</c> is the name Steam prints (the CSV column is called
/// <c>CountryCode</c> but holds e.g. "Austria"); <c>Region</c> is Steam's own
/// region for it, so region totals need no lookup table. <c>DateLocal</c> is a
/// Pacific-time date and the export is sparse, like the daily report.
/// </summary>
public class SteamworksWishlistCountry
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly DateLocal { get; set; }
    public string Country { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int Adds { get; set; }
    public int Deletes { get; set; }
    public int PurchasesAndActivations { get; set; }
    public int Gifts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
