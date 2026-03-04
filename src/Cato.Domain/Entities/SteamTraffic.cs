namespace Cato.Domain.Entities;

public class SteamTraffic
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly TrafficDate { get; set; }
    public int StorePageVisits { get; set; }
    public int UniqueVisitors { get; set; }
    public int Impressions { get; set; }
    public decimal ClickThroughRate { get; set; }
    public int WishlistAdditions { get; set; }
    public int WishlistDeletions { get; set; }
    public int NetWishlistChange { get; set; } // Computed: wishlist_additions - wishlist_deletions
    public int Purchases { get; set; }
    public decimal PurchaseConversionRate { get; set; }
    public string? TrafficSource { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
