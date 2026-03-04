namespace Cato.Domain.Entities;

public class OwnedGameData
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int WishlistAdditions { get; set; }
    public int WishlistDeletions { get; set; }
    public int PurchasesAndActivations { get; set; }
    public int Gifts { get; set; }
    public int PeriodWishlistBalance { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
