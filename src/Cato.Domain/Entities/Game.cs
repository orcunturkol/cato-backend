using System.Text.Json;

namespace Cato.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GameType { get; set; } = "Owned"; // Owned, Competitor, Sourcing, Other
    public DateOnly? ReleaseDate { get; set; }
    public decimal? PriceUsd { get; set; }
    public int DiscountPercent { get; set; }
    public Guid? DeveloperId { get; set; }
    public Guid? PublisherId { get; set; }
    public bool IsEarlyAccess { get; set; }
    public bool IsReleased { get; set; }
    public string? HeaderImageUrl { get; set; }
    public string? CapsuleImageUrl { get; set; }
    public string? ShortDescription { get; set; }
    public string? DetailedDescription { get; set; }
    public string? Website { get; set; }
    public JsonDocument? Platforms { get; set; } // JSONB: {"windows": true, "mac": false, "linux": false}
    public string? SupportedLanguages { get; set; }
    public int? MetacriticScore { get; set; }
    public string? SteamReviewScore { get; set; }
    public int ReviewCount { get; set; }
    public int FollowersCount { get; set; }
    public bool IsFiltered { get; set; }
    public string? FilterReason { get; set; }
    public DateTime? FilteredAt { get; set; }
    public JsonDocument? ContentDescriptorIds { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public LegalEntity? Developer { get; set; }
    public LegalEntity? Publisher { get; set; }
    public ICollection<GameGenre> Genres { get; set; } = [];
    public ICollection<GenreTag> Tags { get; set; } = [];
    public ICollection<SteamSaleFinancial> SalesFinancials { get; set; } = [];
    public ICollection<SteamTraffic> TrafficRecords { get; set; } = [];
    public ICollection<CcuHistory> CcuHistories { get; set; } = [];
    public ICollection<OwnedGameData> OwnedGameData { get; set; } = [];
    public ICollection<GroupMemberCountSnapshot> GroupMemberCountSnapshots { get; set; } = [];
    public ICollection<SteamDbSnapshot> SteamDbSnapshots { get; set; } = [];
    public ICollection<PriceSnapshot> PriceSnapshots { get; set; } = [];
    public ICollection<AppKeyValueSnapshot> AppKeyValueSnapshots { get; set; } = [];
    public ICollection<AppChangeRecord> AppChangeRecords { get; set; } = [];
    public ICollection<GameAction> GameActions { get; set; } = [];
    public ICollection<TargetMatch> TargetMatches { get; set; } = [];
    public ICollection<WishlistInsight> WishlistInsights { get; set; } = [];
    public ICollection<SteamTrafficBreakdown> TrafficBreakdowns { get; set; } = [];
    public ICollection<GameNews> News { get; set; } = [];
    public ICollection<ActiveUsersHistory> ActiveUsersHistories { get; set; } = [];
    public ICollection<DemoDownload> DemoDownloads { get; set; } = [];
    public ICollection<ReviewSummarySnapshot> ReviewSummarySnapshots { get; set; } = [];
    public ICollection<SteamReview> SteamReviews { get; set; } = [];
    public ICollection<GameAchievementSchema> AchievementSchemas { get; set; } = [];
    public ICollection<SteamSpecialEventGame> SteamSpecialEventGames { get; set; } = [];
}
