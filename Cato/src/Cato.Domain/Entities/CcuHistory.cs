namespace Cato.Domain.Entities;

public class CcuHistory
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateTime Timestamp { get; set; }
    public int CcuCount { get; set; }
    public int? PeakCcuToday { get; set; }
    public string Source { get; set; } = "Steam API";
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
