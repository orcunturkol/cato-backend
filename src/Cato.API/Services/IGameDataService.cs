using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Models.SteamDb;

namespace Cato.API.Services;

public interface IGameDataService
{
    Task<List<CcuDto>> GetCcuHistoryAsync(GetCcuHistoryQuery query, CancellationToken ct = default);
    Task<List<FinancialDto>> GetFinancialDataAsync(GetFinancialDataQuery query, CancellationToken ct = default);
    Task<List<TrafficDto>> GetTrafficDataAsync(GetTrafficDataQuery query, CancellationToken ct = default);
    Task<List<OwnedGameDto>> GetOwnedGameDataAsync(GetOwnedGameDataQuery query, CancellationToken ct = default);
    Task<List<SteamworksWishlistDailyDto>> GetSteamworksWishlistDailyAsync(GetSteamworksWishlistDailyQuery query, CancellationToken ct = default);
    Task<List<SteamworksWishlistCohortDto>> GetSteamworksWishlistCohortsAsync(GetSteamworksWishlistCohortsQuery query, CancellationToken ct = default);
    Task<SteamworksWishlistCountriesDto> GetSteamworksWishlistCountriesAsync(GetSteamworksWishlistCountriesQuery query, CancellationToken ct = default);
    Task<SteamworksWishlistSummaryDto> GetSteamworksWishlistSummaryAsync(CancellationToken ct = default);
    Task<List<SalesDailyDto>> GetSalesDailyAsync(GetSalesDailyQuery query, CancellationToken ct = default);
    Task<SalesCountriesDto> GetSalesCountriesAsync(GetSalesCountriesQuery query, CancellationToken ct = default);
    Task<SalesSummaryDto> GetSalesSummaryAsync(CancellationToken ct = default);
    Task<List<StoreTrafficPeriodDto>> GetStoreTrafficPeriodsAsync(GetStoreTrafficPeriodsQuery query, CancellationToken ct = default);
    Task<StoreTrafficBreakdownDto> GetStoreTrafficBreakdownAsync(GetStoreTrafficBreakdownQuery query, CancellationToken ct = default);
    Task<NavStatsDto> GetNavStatsAsync(GetNavStatsQuery query, CancellationToken ct = default);
    Task<PlayersDto> GetPlayersAsync(GetPlayersQuery query, CancellationToken ct = default);
    Task<DemoDownloadsDto> GetDemoDownloadsAsync(GetDemoDownloadsQuery query, CancellationToken ct = default);
    Task<BundlesDto> GetBundlesAsync(GetBundlesQuery query, CancellationToken ct = default);
    Task<BundleDetailDto> GetBundleAsync(GetBundleQuery query, CancellationToken ct = default);
    Task<List<GroupMemberCountDto>> GetGroupMemberCountAsync(GetGroupMemberCountQuery query, CancellationToken ct = default);
    Task<List<FollowerPointDto>> GetFollowerSeriesAsync(GetFollowerSeriesQuery query, CancellationToken ct = default);
    Task<List<SteamDbSnapshotDto>> GetSteamDbSnapshotsAsync(GetSteamDbSnapshotQuery query, CancellationToken ct = default);
    Task<PagedResult<SteamDbRankingDto>> GetSteamDbRankingsAsync(GetSteamDbRankingsQuery query, CancellationToken ct = default);
    Task<List<DateOnly>> GetAvailableDatesAsync(GetAvailableDatesQuery query, CancellationToken ct = default);
}
