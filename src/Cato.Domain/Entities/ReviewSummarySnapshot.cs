namespace Cato.Domain.Entities;

public class ReviewSummarySnapshot
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }

    public int ReviewScore { get; set; }
    public string ReviewScoreDesc { get; set; } = string.Empty;

    public int TotalPositive { get; set; }
    public int TotalNegative { get; set; }
    public int TotalReviews { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
