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

    public async Task<SteamAppData?> GetAppDetailsAsync(int appId, CancellationToken ct = default)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=us&l=english";
        _logger.LogInformation("Fetching Steam store details for AppId {AppId}", appId);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        // The response is a dictionary keyed by the app ID as a string.
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
}
