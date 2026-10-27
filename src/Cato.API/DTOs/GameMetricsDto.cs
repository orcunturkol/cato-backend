using Cato.API.Models.Ingestion;

namespace Cato.API.DTOs;

/// <param name="Followers">Daily follower count of the last N days, oldest first (same merge as /followers).</param>
/// <param name="MostWished">Newest steamdb_most_wished row of the last max(N, 30) days, or null.</param>
/// <param name="WishlistActivity">Newest steamdb_wishlist_activity row of the last max(N, 30) days, or null.</param>
/// <param name="Ccu">CCU of the last 7 days, or null when nothing was collected.</param>
/// <param name="FollowersSince">First day of the SteamDB follower graph, which starts when the store
/// page opened (never before 2019-07-20); null without a graph.</param>
/// <param name="Reviews">The newest review summary, or null.</param>
/// <param name="ReviewsDaily">Stored reviews per day (UTC) of the last N days, oldest first. Reviews are
/// stored for the tracked types only (Sourcing, Owned, OGT).</param>
/// <param name="Achievements">The newest day of global achievement unlock percentages, or null.</param>
public record GameMetricsDto(
    int AppId,
    List<FollowerPointDto> Followers,
    ListRankDto? MostWished,
    ListRankDto? WishlistActivity,
    CcuSummaryDto? Ccu,
    DateOnly? FollowersSince = null,
    ReviewSummaryDto? Reviews = null,
    List<ReviewDayDto>? ReviewsDaily = null,
    AchievementSummaryDto? Achievements = null);

public record ListRankDto(DateOnly Date, int Rank, int Follows, int SevenDayGain);

public record CcuSummaryDto(DateTime LatestAt, int Latest, int Peak7d);

public record ReviewSummaryDto(DateOnly Date, int Total, int Positive, int Negative, string ScoreDesc);

public record ReviewDayDto(DateOnly Date, int Reviews, int Positive);

/// <param name="TopPercent">Unlock percentage of the most unlocked achievement: about the share of owners who played.</param>
public record AchievementSummaryDto(DateOnly Date, int Count, double TopPercent, double MedianPercent);
