using System.Text.Json;
using System.Text.RegularExpressions;
using Cato.Infrastructure.Steam.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam;

public class SteamApiService : ISteamApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamApiService> _logger;
    private readonly SteamWebApiSettings _webApiSettings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SteamApiService(
        HttpClient httpClient,
        ILogger<SteamApiService> logger,
        IOptions<SteamWebApiSettings> webApiSettings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _webApiSettings = webApiSettings.Value;
    }

    private static readonly SemaphoreSlim RateLimiter = new(1, 1);
    private const int DelayBetweenRequestsMs = 1500;
    private const int MaxRetries = 3;

    // Keyed Web API (api.steampowered.com) is throttled independently from the
    // store endpoints, so it gets its own limiter. ~1100ms ≈ 0.9 req/s, well
    // inside the 100k calls/day key budget.
    private static readonly SemaphoreSlim WebApiRateLimiter = new(1, 1);
    private const int WebApiDelayBetweenRequestsMs = 1100;

    public async Task<SteamAppData?> GetAppDetailsAsync(int appId, CancellationToken ct = default)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=us&l=english";
        _logger.LogInformation("Fetching Steam store details for AppId {AppId}", appId);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync(ct);
            try
            {
                var response = await _httpClient.GetAsync(url, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var backoff = (attempt + 1) * 5000;
                    _logger.LogWarning("Rate limited on AppId {AppId}, retrying in {Backoff}ms (attempt {Attempt}/{Max})",
                        appId, backoff, attempt + 1, MaxRetries);
                    await Task.Delay(backoff, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var dict = JsonSerializer.Deserialize<Dictionary<string, SteamAppDetailsWrapper>>(json, JsonOptions);

                if (dict is null || !dict.TryGetValue(appId.ToString(), out var wrapper))
                {
                    _logger.LogWarning("Steam API returned no data for AppId {AppId}", appId);
                    return null;
                }

                if (!wrapper.Success || wrapper.Data is null)
                {
                    _logger.LogWarning("Steam API returned success=false for AppId {AppId}", appId);
                    return null;
                }

                _logger.LogInformation("Successfully fetched Steam data for '{Name}' (AppId {AppId})", wrapper.Data.Name, appId);
                return wrapper.Data;
            }
            finally
            {
                // Enforce delay before next request can proceed
                _ = Task.Run(async () =>
                {
                    await Task.Delay(DelayBetweenRequestsMs, CancellationToken.None);
                    RateLimiter.Release();
                }, CancellationToken.None);
            }
        }

        _logger.LogError("Failed to fetch Steam data for AppId {AppId} after {MaxRetries} retries", appId, MaxRetries);
        return null;
    }

    private static readonly Regex TagRegex = new(
        @"class=""app_tag""[^>]*>\s*(.+?)\s*</a>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public async Task<List<SteamUserTag>> GetUserTagsAsync(int appId, CancellationToken ct = default)
    {
        var url = $"https://store.steampowered.com/app/{appId}?l=english";
        _logger.LogInformation("Fetching user tags for AppId {AppId}", appId);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync(ct);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Cookie", "birthtime=0; mature_content=1; lastagecheckage=1-0-2000");

                var response = await _httpClient.SendAsync(request, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var backoff = (attempt + 1) * 5000;
                    _logger.LogWarning("Rate limited fetching tags for AppId {AppId}, retrying in {Backoff}ms (attempt {Attempt}/{Max})",
                        appId, backoff, attempt + 1, MaxRetries);
                    await Task.Delay(backoff, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Steam store page returned {StatusCode} for AppId {AppId}", response.StatusCode, appId);
                    return [];
                }

                var html = await response.Content.ReadAsStringAsync(ct);

                var tagsBlockMatch = Regex.Match(html, @"glance_tags popular_tags.*?</div>", RegexOptions.Singleline);
                if (!tagsBlockMatch.Success)
                {
                    _logger.LogWarning("No user tags section found for AppId {AppId}", appId);
                    return [];
                }

                var tags = new List<SteamUserTag>();
                var rank = 1;
                foreach (Match match in TagRegex.Matches(tagsBlockMatch.Value))
                {
                    var name = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        tags.Add(new SteamUserTag { Name = name, Rank = rank++ });
                    }
                }

                _logger.LogInformation("Fetched {Count} user tags for AppId {AppId}", tags.Count, appId);
                return tags;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error fetching user tags for AppId {AppId} (attempt {Attempt}/{Max})",
                    appId, attempt + 1, MaxRetries);
                if (attempt == MaxRetries) break;
            }
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(DelayBetweenRequestsMs, CancellationToken.None);
                    RateLimiter.Release();
                }, CancellationToken.None);
            }
        }

        _logger.LogWarning("Failed to fetch user tags for AppId {AppId}, returning empty list", appId);
        return [];
    }

    public async Task<SteamAppReviewsResponse?> GetAppReviewsAsync(
        int appId, string cursor = "*", CancellationToken ct = default)
    {
        // Steam cursors contain base64 characters including '+' and '/' — must be URL-encoded.
        var encodedCursor = Uri.EscapeDataString(cursor);
        var url = $"https://store.steampowered.com/appreviews/{appId}" +
                  $"?json=1&filter=recent&language=all&num_per_page=100&cursor={encodedCursor}";

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync(ct);
            try
            {
                var response = await _httpClient.GetAsync(url, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < MaxRetries)
                    {
                        var backoff = (attempt + 1) * 5000;
                        await Task.Delay(backoff, ct);
                        continue;
                    }
                    _logger.LogWarning("Steam appreviews rate-limited for AppId {AppId} after {Retries} retries", appId, MaxRetries);
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Steam appreviews returned {Status} for AppId {AppId}", response.StatusCode, appId);
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<SteamAppReviewsResponse>(content, JsonOptions);

                if (result?.Success != 1)
                    return null;

                return result;
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "HTTP error fetching reviews for AppId {AppId}, attempt {Attempt}", appId, attempt + 1);
                continue;
            }
            finally
            {
                // Enforce minimum delay before the next request can proceed — mirrors existing pattern.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(DelayBetweenRequestsMs, CancellationToken.None);
                    RateLimiter.Release();
                }, CancellationToken.None);
            }
        }

        return null;
    }

    public async Task<SteamPlayerSummariesResponse?> GetPlayerSummariesAsync(
        IReadOnlyList<long> steamIds, CancellationToken ct = default)
    {
        if (steamIds.Count == 0)
            return new SteamPlayerSummariesResponse();
        if (steamIds.Count > 100)
            throw new ArgumentException("GetPlayerSummaries accepts at most 100 steamids per call; chunk before calling.", nameof(steamIds));

        // The URL embeds the API key — never log it; log only counts and status codes.
        var url = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/" +
                  $"?key={_webApiSettings.ApiKey}&steamids={string.Join(",", steamIds)}";

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await WebApiRateLimiter.WaitAsync(ct);
            try
            {
                var response = await _httpClient.GetAsync(url, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < MaxRetries)
                    {
                        var backoff = (attempt + 1) * 5000;
                        await Task.Delay(backoff, ct);
                        continue;
                    }
                    _logger.LogWarning("GetPlayerSummaries rate-limited for {Count} ids after {Retries} retries", steamIds.Count, MaxRetries);
                    return null;
                }

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    var forbiddenBody = await response.Content.ReadAsStringAsync(ct);
                    if (LooksLikeInvalidKey(forbiddenBody))
                        _logger.LogError("GetPlayerSummaries returned {Status} — invalid or missing Steam Web API key", response.StatusCode);
                    else
                        _logger.LogWarning("GetPlayerSummaries returned {Status} for {Count} ids", response.StatusCode, steamIds.Count);
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetPlayerSummaries returned {Status} for {Count} ids", response.StatusCode, steamIds.Count);
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<SteamPlayerSummariesResponse>(content, JsonOptions);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "HTTP error fetching player summaries for {Count} ids, attempt {Attempt}", steamIds.Count, attempt + 1);
                continue;
            }
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(WebApiDelayBetweenRequestsMs, CancellationToken.None);
                    WebApiRateLimiter.Release();
                }, CancellationToken.None);
            }
        }

        return null;
    }

    public async Task<SteamSchemaForGameResponse?> GetSchemaForGameAsync(
        int appId, CancellationToken ct = default)
    {
        // The URL embeds the API key — never log it; log only appid and status codes.
        var url = "https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/" +
                  $"?key={_webApiSettings.ApiKey}&appid={appId}&l=english";

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await WebApiRateLimiter.WaitAsync(ct);
            try
            {
                var response = await _httpClient.GetAsync(url, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < MaxRetries)
                    {
                        var backoff = (attempt + 1) * 5000;
                        await Task.Delay(backoff, ct);
                        continue;
                    }
                    _logger.LogWarning("GetSchemaForGame rate-limited for AppId {AppId} after {Retries} retries", appId, MaxRetries);
                    return null;
                }

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    var forbiddenBody = await response.Content.ReadAsStringAsync(ct);
                    if (LooksLikeInvalidKey(forbiddenBody))
                        _logger.LogError("GetSchemaForGame returned {Status} — invalid or missing Steam Web API key", response.StatusCode);
                    else
                        _logger.LogWarning("GetSchemaForGame returned {Status} for AppId {AppId} (app likely restricted / no public schema)", response.StatusCode, appId);
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GetSchemaForGame returned {Status} for AppId {AppId}", response.StatusCode, appId);
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<SteamSchemaForGameResponse>(content, JsonOptions);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "HTTP error fetching schema for AppId {AppId}, attempt {Attempt}", appId, attempt + 1);
                continue;
            }
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(WebApiDelayBetweenRequestsMs, CancellationToken.None);
                    WebApiRateLimiter.Release();
                }, CancellationToken.None);
            }
        }

        return null;
    }

    public async Task<SteamPlayerAchievementsResponse?> GetPlayerAchievementsAsync(
        long steamId64, int appId, CancellationToken ct = default)
    {
        // The URL embeds the API key — never log it; log only ids and status codes.
        var url = "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/" +
                  $"?key={_webApiSettings.ApiKey}&steamid={steamId64}&appid={appId}&l=english";

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await WebApiRateLimiter.WaitAsync(ct);
            try
            {
                var response = await _httpClient.GetAsync(url, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < MaxRetries)
                    {
                        var backoff = (attempt + 1) * 5000;
                        await Task.Delay(backoff, ct);
                        continue;
                    }
                    _logger.LogWarning("GetPlayerAchievements rate-limited for AppId {AppId} after {Retries} retries", appId, MaxRetries);
                    return null;
                }

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    var forbiddenBody = await response.Content.ReadAsStringAsync(ct);
                    if (LooksLikeInvalidKey(forbiddenBody))
                    {
                        _logger.LogError("GetPlayerAchievements returned {Status} — invalid or missing Steam Web API key", response.StatusCode);
                        return null;
                    }

                    // A 403 with a valid key means the reviewer's profile is
                    // private/friends-only. Surface it as a per-pair business
                    // outcome so the watcher classifies it Private and applies the
                    // long back-off, instead of treating it as a transient miss and
                    // retrying the same private profile every cycle.
                    return new SteamPlayerAchievementsResponse
                    {
                        PlayerStats = new SteamPlayerStats { Success = false, Error = "Profile is not public" },
                    };
                }

                // Private profiles surface as 400/500 with a body describing the
                // reason; a 2xx with success=false carries it in the JSON. Either
                // way we want to read the body when present, so only treat a
                // hard non-success WITHOUT a body as a transient miss.
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogWarning("GetPlayerAchievements returned {Status} for AppId {AppId}", response.StatusCode, appId);
                    return null;
                }

                try
                {
                    return JsonSerializer.Deserialize<SteamPlayerAchievementsResponse>(content, JsonOptions);
                }
                catch (JsonException)
                {
                    // Non-JSON error body on a non-2xx — treat as a per-pair failure
                    // (Success stays false) rather than a transient null.
                    if (!response.IsSuccessStatusCode)
                        return new SteamPlayerAchievementsResponse();
                    throw;
                }
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "HTTP error fetching achievements for AppId {AppId}, attempt {Attempt}", appId, attempt + 1);
                continue;
            }
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(WebApiDelayBetweenRequestsMs, CancellationToken.None);
                    WebApiRateLimiter.Release();
                }, CancellationToken.None);
            }
        }

        return null;
    }

    /// <summary>
    /// Distinguishes a genuine bad/missing key from an ordinary permission 403.
    /// Steam answers an invalid/missing key with an HTML page ("Access is denied …
    /// please verify your <c>key=</c> parameter"); a 401/403 lacking that signature
    /// on a per-user endpoint means the data is private, not that the key is bad.
    /// </summary>
    private static bool LooksLikeInvalidKey(string? body) =>
        !string.IsNullOrEmpty(body)
        && (body.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || body.Contains("verify your", StringComparison.OrdinalIgnoreCase));
}
