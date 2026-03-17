using System.Text.Json;
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
}
