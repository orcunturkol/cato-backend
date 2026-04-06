namespace Cato.Domain.Entities;

public class GameAction
{
    public Guid Id { get; set; }
    public Guid ActionId { get; set; }
    public Guid GameId { get; set; }
    public string GameRole { get; set; } = "Primary"; // Primary, Secondary, Featured, Included
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public MarketingAction Action { get; set; } = null!;
    public Game Game { get; set; } = null!;
}
