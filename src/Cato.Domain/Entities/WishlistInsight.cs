namespace Cato.Domain.Entities;

public class WishlistInsight
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int RelatedAppId { get; set; }
    public string RelatedName { get; set; } = string.Empty;
    public decimal LinkScore { get; set; }
    public decimal Price { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string[] Genres { get; set; } = [];
    public long CopiesSold { get; set; }
    public decimal Revenue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
