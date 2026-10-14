namespace Cato.Domain.Entities;

/// <summary>
/// One day of the Steamworks wishlist action report (partner CSV
/// <c>QueryWishlistActionsForCSV</c>) for an owned game.
///
/// <c>DateLocal</c> is a Pacific-time date, as Steam reports it. The export is
/// sparse: a day with no activity at all has no row, so a missing day means zero,
/// not "unknown".
/// </summary>
public class SteamworksWishlistDaily
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly DateLocal { get; set; }
    public int Adds { get; set; }
    public int Deletes { get; set; }
    public int PurchasesAndActivations { get; set; }
    public int Gifts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
