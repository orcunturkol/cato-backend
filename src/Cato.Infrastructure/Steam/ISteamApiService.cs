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
}
