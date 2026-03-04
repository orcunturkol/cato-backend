namespace Cato.Infrastructure.Steam.SteamKit;

public interface ISteamKitService
{
    /// <summary>
    /// Returns the current concurrent player count for a given Steam AppId,
    /// or null if the request fails.
    /// </summary>
    Task<int?> GetCurrentPlayerCountAsync(int appId, CancellationToken ct = default);

    /// <summary>
    /// Returns all AppIds that have changed since the given PICS change number,
    /// along with the latest change number seen.
    /// </summary>
    Task<(IReadOnlyList<uint> AppIds, uint LatestChangeNumber)> GetChangedAppIdsSinceAsync(
        uint sinceChangeNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Returns PICS metadata for a specific AppId, or null if unavailable.
    /// </summary>
    Task<SteamPicsAppInfo?> GetAppInfoAsync(uint appId, CancellationToken ct = default);

    /// <summary>Returns true if SteamKit2 is currently connected and logged in.</summary>
    bool IsConnected { get; }
}
