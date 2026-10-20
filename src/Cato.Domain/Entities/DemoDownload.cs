namespace Cato.Domain.Entities;

/// <summary>
/// Demo downloads of one region or country over <c>SnapshotDate</c> .. <c>PeriodEnd</c>
/// (both inclusive), from the Steamworks "Downloads by Region" CSV of the game's demo.
/// The daily job stores one-day periods; history is backfilled as whole months, never
/// overlapping. The manual upload files its rows under the upload day.
/// </summary>
public class DemoDownload
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public int? DemoAppId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string GeoType { get; set; } = string.Empty;   // "Region" | "Country"
    public string GeoName { get; set; } = string.Empty;
    public long TotalDownloads { get; set; }
    public decimal? SharePercent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
