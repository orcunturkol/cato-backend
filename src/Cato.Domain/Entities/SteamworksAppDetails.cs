namespace Cato.Domain.Entities;

/// <summary>
/// A daily snapshot of the Steamworks app details page of an owned game or of its
/// demo (<c>IsDemo</c>; <c>GameId</c> is then the main game): median time played and
/// lifetime unique users. Steam shows only the current values.
/// </summary>
public class SteamworksAppDetails
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public int AppId { get; set; }
    public bool IsDemo { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int? MedianPlaytimeMinutes { get; set; }
    // Steam's own wording ("3 hours 43 minutes"); kept when it cannot be parsed.
    public string? MedianPlaytimeText { get; set; }
    public long? LifetimeUniqueUsers { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
