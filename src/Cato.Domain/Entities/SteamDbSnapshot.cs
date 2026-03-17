namespace Cato.Domain.Entities;

public class SteamDbSnapshot
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string Source { get; set; } = string.Empty; // "steamdb_most_wished" or "steamdb_wishlist_activity"
    public int Rank { get; set; }
    public string? Price { get; set; }
    public string? Rating { get; set; }
    public string? Release { get; set; }
    public int Follows { get; set; }
    public int SevenDayGain { get; set; }
    public DateTime ScrapedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
