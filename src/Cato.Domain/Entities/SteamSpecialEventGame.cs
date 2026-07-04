namespace Cato.Domain.Entities;

/// <summary>
/// Membership of a game in a special event. FirstSeenAt is our derived
/// "joined the event" timestamp; SteamDisplayedStart/End hold the per-game
/// discount window when the optional store-item enrichment supplies it.
/// </summary>
public class SteamSpecialEventGame
{
    public Guid Id { get; set; }
    public Guid SteamSpecialEventId { get; set; }
    public Guid GameId { get; set; }
    public string ItemType { get; set; } = "game"; // "game" | "demo" (per Steam's tagged_items)
    public DateTime? SteamDisplayedStart { get; set; }
    public DateTime? SteamDisplayedEnd { get; set; }
    public int? DiscountPercent { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public SteamSpecialEvent SteamSpecialEvent { get; set; } = null!;
    public Game Game { get; set; } = null!;
}
