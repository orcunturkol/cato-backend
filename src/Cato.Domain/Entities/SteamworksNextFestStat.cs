namespace Cato.Domain.Entities;

/// <summary>
/// One cumulative snapshot of a game during a Steam Next Fest (Steamworks recap page,
/// <c>demo_stats</c>): demo players, wishlists and wishlists from demo players from the
/// fest start up to <c>SnapshotEnd</c> (about one per day). <c>FestKey</c> is Steam's
/// sale page id ("sale_nextfest_june_2025").
/// </summary>
public class SteamworksNextFestStat
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public int AppId { get; set; }
    public int? DemoAppId { get; set; }
    public string FestKey { get; set; } = string.Empty;
    public DateTime? EventStart { get; set; }
    public DateTime? EventEnd { get; set; }
    public DateTime SnapshotEnd { get; set; }
    public int DemoPlayerCount { get; set; }
    public int WishlistCount { get; set; }
    public int PlayerWishlistCount { get; set; }
    public long? FestPageViews { get; set; }
    public long? FestPeriodPageViews { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
