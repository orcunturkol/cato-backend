namespace Cato.Domain.Entities;

public class ReviewSummarySnapshot
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }

    /// <summary>
    /// Steam numeric review score 0–9 (e.g. 8 = Very Positive)
    /// </summary>
    public int ReviewScore { get; set; }

    /// <summary>
    /// Human-readable Steam score label, e.g. "Very Positive"
    /// </summary>
    public string ReviewScoreDesc { get; set; } = string.Empty;

    public int TotalPositive { get; set; }
    public int TotalNegative { get; set; }
    public int TotalReviews { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
