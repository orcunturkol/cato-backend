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

    /// <summary>
    /// Fetches a page of recent reviews for the given appId from the public Steam appreviews endpoint.
    /// Pass cursor="*" for the first page; use the cursor from the previous response for subsequent pages.
    /// Returns null on failure or when Steam returns success != 1.
    /// </summary>
    Task<SteamAppReviewsResponse?> GetAppReviewsAsync(int appId, string cursor = "*", CancellationToken ct = default);
}
