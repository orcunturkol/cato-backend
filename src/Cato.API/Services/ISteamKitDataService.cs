namespace Cato.API.Services;

public interface ISteamKitDataService
{
    /// <summary>Fetches live CCU from Steam for the given AppId.</summary>
    Task<int?> GetLiveCcuAsync(int appId, CancellationToken ct = default);

    /// <summary>Fetches live CCU and saves it as a CcuHistory record (Source = "SteamKit2").</summary>
    Task<int?> SaveLiveCcuAsync(int appId, CancellationToken ct = default);
}
