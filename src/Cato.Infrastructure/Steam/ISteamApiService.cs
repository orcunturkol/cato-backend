using Cato.Infrastructure.Steam.Models;

namespace Cato.Infrastructure.Steam;

public interface ISteamApiService
{
    /// <summary>
    /// Fetch full app details from the Steam Store API.
    /// </summary>
    Task<SteamAppData?> GetAppDetailsAsync(int appId, CancellationToken ct = default);

    /// <summary>
    /// Fetch popular user-defined tags from the Steam store page.
    /// </summary>
    Task<List<SteamUserTag>> GetUserTagsAsync(int appId, CancellationToken ct = default);
}
