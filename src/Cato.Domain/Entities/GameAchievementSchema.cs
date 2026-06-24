namespace Cato.Domain.Entities;

/// <summary>
/// One achievement a game defines (the catalog/denominator), sourced from
/// ISteamUserStats/GetSchemaForGame/v2. Related to main_game by GameId; AppId
/// is denormalized so player-achievement value-joins don't need the Game row.
/// Latest-only upsert keyed by (GameId, ApiName).
/// </summary>
public class GameAchievementSchema
{
    public Guid Id { get; set; }

    /// <summary>FK to Game.Id.</summary>
    public Guid GameId { get; set; }

    /// <summary>Steam AppID, denormalized for value-joins against player achievements.</summary>
    public int AppId { get; set; }

    /// <summary>Steam internal achievement key (the "name" field), e.g. "ACH_WIN_ONE_GAME".</summary>
    public string ApiName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
    public string? Description { get; set; }

    /// <summary>True if the achievement is hidden until unlocked.</summary>
    public bool Hidden { get; set; }

    public string? IconUrl { get; set; }
    public string? IconGrayUrl { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;

    /// <summary>Player unlocks that reference this catalog achievement.</summary>
    public ICollection<SteamPlayerAchievement> PlayerAchievements { get; set; } = [];
}
