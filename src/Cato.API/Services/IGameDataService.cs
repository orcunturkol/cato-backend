using Cato.API.Models.Ingestion;

namespace Cato.API.Services;

public interface IGameDataService
{
    Task<List<CcuDto>> GetCcuHistoryAsync(GetCcuHistoryQuery query, CancellationToken ct = default);
    Task<List<FinancialDto>> GetFinancialDataAsync(GetFinancialDataQuery query, CancellationToken ct = default);
    Task<List<TrafficDto>> GetTrafficDataAsync(GetTrafficDataQuery query, CancellationToken ct = default);
    Task<List<OwnedGameDto>> GetOwnedGameDataAsync(GetOwnedGameDataQuery query, CancellationToken ct = default);
}
