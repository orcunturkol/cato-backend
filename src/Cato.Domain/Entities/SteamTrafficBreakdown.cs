namespace Cato.Domain.Entities;

public class SteamTrafficBreakdown
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string PageCategory { get; set; } = string.Empty;
    public string PageFeature { get; set; } = string.Empty;
    public long Impressions { get; set; }
    public long Visits { get; set; }
    public DateTime CreatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
