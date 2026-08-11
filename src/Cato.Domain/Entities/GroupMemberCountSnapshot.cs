namespace Cato.Domain.Entities;

public class GroupMemberCountSnapshot
{
    /// <summary>Live daily scrape of the app's community hub (run_daily_db).</summary>
    public const string SteamCommunitySource = "steam_community_group";

    /// <summary>Historical backfill of the same metric from SteamDB's follower graph.</summary>
    public const string SteamDbFollowerSource = "steamdb_follower_history";

    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }

    /// <summary>
    /// Which collector produced this row. Both series measure the same thing —
    /// followers of the game's Steam community hub — but the SteamDB backfill
    /// reaches years further back, so they are kept side by side rather than merged.
    /// </summary>
    public string Source { get; set; } = SteamCommunitySource;

    public int MemberCount { get; set; }
    public string? Error { get; set; }
    public DateTime ScrapedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
