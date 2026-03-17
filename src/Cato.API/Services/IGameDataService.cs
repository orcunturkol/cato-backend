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
    Task<List<GroupMemberCountDto>> GetGroupMemberCountAsync(GetGroupMemberCountQuery query, CancellationToken ct = default);
    Task<List<SteamDbSnapshotDto>> GetSteamDbSnapshotsAsync(GetSteamDbSnapshotQuery query, CancellationToken ct = default);
    Task<PagedResult<SteamDbRankingDto>> GetSteamDbRankingsAsync(GetSteamDbRankingsQuery query, CancellationToken ct = default);
    Task<List<DateOnly>> GetAvailableDatesAsync(GetAvailableDatesQuery query, CancellationToken ct = default);
}
