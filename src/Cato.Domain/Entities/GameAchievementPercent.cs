namespace Cato.Domain.Entities;

/// <summary>
/// A daily snapshot of one achievement's global unlock percentage
/// (ISteamUserStats/GetGlobalAchievementPercentagesForApp, no key) for an owned game.
/// Names and icons are in <c>game_achievement_schema</c> (same <c>ApiName</c>).
/// </summary>
public class GameAchievementPercent
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public int AppId { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public DateOnly SnapshotDate { get; set; }
    public decimal Percent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
