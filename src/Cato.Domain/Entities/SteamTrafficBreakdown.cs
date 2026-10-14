namespace Cato.Domain.Entities;

/// <summary>
/// One row of the Steamworks store traffic CSV (<c>/apps/navtrafficstats</c>): the
/// totals of one page category/feature over the period <c>SnapshotDate</c> ..
/// <c>PeriodEnd</c> (GMT days, both inclusive). The daily job stores one-day
/// periods; history is backfilled as whole months, so periods never overlap.
///
/// The CSV has no total row. Rows outside the <c>Country</c> category add up to the
/// page's totals (<c>Bot Traffic</c> carries visits only); the <c>Country</c> rows are
/// a second split of the same totals, with the country code as the feature.
/// </summary>
public class SteamTrafficBreakdown
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string PageCategory { get; set; } = string.Empty;
    public string PageFeature { get; set; } = string.Empty;
    public long Impressions { get; set; }
    public long Visits { get; set; }
    // Impressions and visits from users who own the game.
    public long OwnerImpressions { get; set; }
    public long OwnerVisits { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
