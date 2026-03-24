using System.Text.Json;
using System.Text.RegularExpressions;
using Cato.Infrastructure.Steam.Models;
using Microsoft.Extensions.Logging;

namespace Cato.Infrastructure.Steam;

public class SteamApiService : ISteamApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamApiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SteamApiService(HttpClient httpClient, ILogger<SteamApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private static readonly SemaphoreSlim RateLimiter = new(1, 1);
    private const int DelayBetweenRequestsMs = 1500;
    private const int MaxRetries = 3;

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
}
