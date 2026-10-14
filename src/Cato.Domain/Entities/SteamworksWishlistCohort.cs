namespace Cato.Domain.Entities;

/// <summary>
/// One row of the Steamworks "Wishlist Conversions by Cohort" report (partner CSV
/// <c>QueryWishlistCohortForCSV</c>): conversions on <c>DateLocal</c> from users who
/// wishlisted the game in <c>MonthCohort</c>.
///
/// <c>DateLocal</c> is a Pacific-time date; <c>MonthCohort</c> is the first day of
/// the month the wishlist was added. The export only lists non-zero pairs, so a
/// missing (DateLocal, MonthCohort) means zero conversions.
/// </summary>
public class SteamworksWishlistCohort
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly DateLocal { get; set; }
    public DateOnly MonthCohort { get; set; }
    public int PurchasesAndActivations { get; set; }
    public int Gifts { get; set; }
    public int TotalConversions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
