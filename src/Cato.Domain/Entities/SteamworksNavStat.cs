namespace Cato.Domain.Entities;

/// <summary>
/// One product row of the Steamworks publisher Nav Stats CSV
/// (<c>/pub/navstats/&lt;publisher&gt;</c>): a product's impressions and visits over
/// <c>PeriodStart</c> .. <c>PeriodEnd</c> (GMT days, both inclusive) for one traffic
/// source filter and one region filter. The daily job stores one-day periods;
/// history is backfilled as whole months, so periods never overlap.
///
/// Keyed by app id, not by game: the CSV also lists the publisher's demos and
/// playtests, which CATO does not track. <c>Attribution</c> "all" is the whole;
/// "store", "steamworks" (Steam platform) and "external" are its parts. A
/// product's "all" visits equal its store traffic visits plus bot visits.
/// </summary>
public class SteamworksNavStat
{
    public Guid Id { get; set; }
    public int PublisherId { get; set; }
    public int AppId { get; set; }
    public string? AppName { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Attribution { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public long Impressions { get; set; }
    public long Visits { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
