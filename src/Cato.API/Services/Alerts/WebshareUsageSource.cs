using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Cato.API.Services.Alerts;

/// <summary>The Webshare plan's bandwidth in the current billing period.</summary>
public sealed record WebshareUsage(long UsedBytes, long LimitBytes, DateTime PeriodStart, DateTime PeriodEnd);

/// <summary>
/// One read of the Webshare account. Usage null and Failed false: no API key is set
/// (Webshare:ApiKey), so the rule is off. Failed: the API could not be read.
/// </summary>
public sealed record WebshareUsageRead(WebshareUsage? Usage, bool Failed);

/// <summary>The Webshare bandwidth for the webshare_bandwidth_low rule.</summary>
public interface IWebshareUsageSource
{
    /// <summary>The last read, at most <see cref="WebshareUsageSource.MaxAge"/> old.</summary>
    Task<WebshareUsageRead> GetAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// Reads the Webshare API (singleton, cached): the subscription's renewal term (start_date,
/// end_date), its active plan's bandwidth limit (GB, counted as GiB: the 3 GB plan ran out at
/// 3,229,801,369 bytes on 2026-11-12) and the gateway's bytes (stats/aggregate
/// bandwidth_total) since the term start. A failed read is kept too,
/// so a broken API is tried (and logged) once per <see cref="MaxAge"/>, not every cycle.
/// </summary>
public sealed class WebshareUsageSource(
    IHttpClientFactory httpFactory, IConfiguration configuration, ILogger<WebshareUsageSource> logger) : IWebshareUsageSource
{
    public const string HttpClientName = "webshare";
    public const string ApiKeySetting = "Webshare:ApiKey";
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    private const string BaseUrl = "https://proxy.webshare.io/api/v2/";
    private const long BytesPerGb = 1L << 30;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTime At, WebshareUsageRead Read)? _last;

    public async Task<WebshareUsageRead> GetAsync(DateTime nowUtc, CancellationToken ct)
    {
        var key = configuration[ApiKeySetting];
        if (string.IsNullOrWhiteSpace(key))
            return new WebshareUsageRead(null, false);

        await _lock.WaitAsync(ct);
        try
        {
            if (_last is not { } last || nowUtc - last.At >= MaxAge)
            {
                WebshareUsageRead read;
                try
                {
                    read = new WebshareUsageRead(await ReadAsync(key.Trim(), nowUtc, ct), false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Alert cycle could not read the Webshare usage: {Error}", ex.Message);
                    read = new WebshareUsageRead(null, true);
                }
                _last = (nowUtc, read);
                last = _last.Value;
            }
            return last.Read;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<WebshareUsage?> ReadAsync(string key, DateTime nowUtc, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);

        using var subscription = await GetJsonAsync(http, key, "subscription/", ct);
        var start = subscription.RootElement.GetProperty("start_date").GetDateTime().ToUniversalTime();
        var end = subscription.RootElement.GetProperty("end_date").GetDateTime().ToUniversalTime();

        var planId = subscription.RootElement.GetProperty("plan").GetInt64();

        using var plan = await GetJsonAsync(http, key, $"subscription/plan/{planId}/", ct);
        // 0 means no bandwidth limit: nothing to run out of.
        var limitGb = plan.RootElement.GetProperty("bandwidth_limit").GetDouble();
        if (limitGb <= 0)
            return null;

        var range = $"timestamp__gte={Iso(start)}&timestamp__lte={Iso(nowUtc)}";
        using var stats = await GetJsonAsync(http, key, $"stats/aggregate/?{range}", ct);
        var used = stats.RootElement.GetProperty("bandwidth_total").GetInt64();

        return new WebshareUsage(used, (long)(limitGb * BytesPerGb), start, end);
    }

    private static string Iso(DateTime utc) =>
        Uri.EscapeDataString(utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string key, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GET {path.Split('?')[0]} answered {(int)response.StatusCode}");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(body, cancellationToken: ct);
    }
}
