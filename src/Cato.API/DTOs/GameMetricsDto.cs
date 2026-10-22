using Cato.API.Models.Ingestion;

namespace Cato.API.DTOs;

/// <param name="Followers">Daily follower count of the last N days, oldest first (same merge as /followers).</param>
/// <param name="MostWished">Newest steamdb_most_wished row of the last max(N, 30) days, or null.</param>
/// <param name="WishlistActivity">Newest steamdb_wishlist_activity row of the last max(N, 30) days, or null.</param>
/// <param name="Ccu">CCU of the last 7 days, or null when nothing was collected.</param>
public record GameMetricsDto(
    int AppId,
    List<FollowerPointDto> Followers,
    ListRankDto? MostWished,
    ListRankDto? WishlistActivity,
    CcuSummaryDto? Ccu);

public record ListRankDto(DateOnly Date, int Rank, int Follows, int SevenDayGain);

public record CcuSummaryDto(DateTime LatestAt, int Latest, int Peak7d);
