using Cato.Infrastructure.Steam.Models;

namespace Cato.Infrastructure.Steam;

public interface ISteamApiService
{
    /// <summary>
    /// Fetch full app details from the Steam Store API.
    /// </summary>
    Task<SteamAppData?> GetAppDetailsAsync(int appId, CancellationToken ct = default);

    /// <summary>
    /// Reads the store prices of up to 100 apps in one app details call
    /// (<c>filters=price_overview</c>). The answer holds only apps with a price: a free
    /// game and an app without a store page are absent. Throws when Steam fails.
    /// </summary>
    Task<IReadOnlyDictionary<int, SteamPriceOverview>> GetPriceOverviewsAsync(
        IReadOnlyList<int> appIds, CancellationToken ct = default);

    /// <summary>
    /// Fetch popular user-defined tags from the Steam store page.
    /// </summary>
    Task<List<SteamUserTag>> GetUserTagsAsync(int appId, CancellationToken ct = default);

    /// <summary>
    /// Fetches a page of recent reviews (all languages) for the given appId from the Web API's
    /// IUserReviewsService/GetAppReviews, without a key.
    /// Pass cursor="*" for the first page; use the cursor from the previous response for subsequent pages.
    /// Returns null on an HTTP failure; an empty page ends the list.
    /// </summary>
    Task<SteamAppReviewsResponse?> GetAppReviewsAsync(int appId, string cursor = "*", CancellationToken ct = default);

    /// <summary>
    /// Fetches player summaries for up to 100 SteamID64s in one call
    /// (ISteamUser/GetPlayerSummaries/v2, requires a Web API key).
    /// Returns null on transient failure — callers should NOT count that
    /// against the requested ids. Steam silently omits invalid/deleted
    /// steamids from a successful response, so callers must diff requested
    /// vs returned ids and treat omissions as per-id failures.
    /// </summary>
    Task<SteamPlayerSummariesResponse?> GetPlayerSummariesAsync(
        IReadOnlyList<long> steamIds, CancellationToken ct = default);

    /// <summary>
    /// Fetches the achievement schema (catalog) for a game
    /// (ISteamUserStats/GetSchemaForGame/v2, requires a Web API key).
    /// Returns null on transient/auth failure. A non-null response with an
    /// empty achievements list means the game defines no achievements — a
    /// valid result, not a failure.
    /// </summary>
    Task<SteamSchemaForGameResponse?> GetSchemaForGameAsync(int appId, CancellationToken ct = default);

    /// <summary>
    /// Fetches one player's achievements for one game
    /// (ISteamUserStats/GetPlayerAchievements/v1, requires a Web API key).
    /// Returns null ONLY on transient/auth HTTP failure. On any 2xx the
    /// deserialized response is returned — callers must inspect
    /// PlayerStats.Success: false is a per-pair business outcome (private
    /// profile, no stats, not owned) described by PlayerStats.Error, not a
    /// transient failure.
    /// </summary>
    Task<SteamPlayerAchievementsResponse?> GetPlayerAchievementsAsync(
        long steamId64, int appId, CancellationToken ct = default);

    /// <summary>
    /// Global unlock percentage of each achievement of a game
    /// (ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2, no key). An app
    /// without stats is an empty list; null only on a transient failure.
    /// </summary>
    Task<List<SteamAchievementPercent>?> GetGlobalAchievementPercentagesAsync(int appId, CancellationToken ct = default);

    /// <summary>
    /// The newest news items of a game (ISteamNews/GetNewsForApp/v2, no key), full
    /// contents. Null only on a transient failure.
    /// </summary>
    Task<List<SteamNewsItem>?> GetNewsForAppAsync(int appId, int count, CancellationToken ct = default);
}
