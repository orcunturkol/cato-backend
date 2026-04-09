namespace Cato.Domain.Entities;

public class DemoDownload
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public int? DemoAppId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string GeoType { get; set; } = string.Empty;   // "Region" | "Country"
    public string GeoName { get; set; } = string.Empty;
    public long TotalDownloads { get; set; }
    public decimal? SharePercent { get; set; }
    public DateTime CreatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
