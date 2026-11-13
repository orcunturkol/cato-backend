using System.Text.Json;

namespace Cato.API.Services.Proxies;

/// <summary>The Evomi Core Residential balance and the traffic of the last 7 days, in bytes.</summary>
public sealed record EvomiUsage(long BalanceBytes, long? UsedLast7DaysBytes);

/// <summary>Usage null and Failed false: no API key (Evomi:ApiKey). Failed: the API could not be read.</summary>
public sealed record EvomiUsageRead(EvomiUsage? Usage, bool Failed);

public interface IEvomiUsageSource
{
    Task<EvomiUsageRead> GetAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// Reads the Evomi public API (singleton, cached 30 min, a failed read too): `GET /public`
/// gives products.rpc.balance_mb (Core Residential), `GET /public/usage?product=rpc&amp;period=7d`
/// gives meta.total_bandwidth_mb. Evomi counts 1 GB as 1,000 MB (docs.evomi.com/public-api,
/// checked 2026-11-13).
/// </summary>
public sealed class EvomiUsageSource(
    IHttpClientFactory httpFactory, IConfiguration configuration, ILogger<EvomiUsageSource> logger) : IEvomiUsageSource
{
    public const string HttpClientName = "evomi";
    public const string ApiKeySetting = "Evomi:ApiKey";
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    private const string BaseUrl = "https://api.evomi.com/public";
    private const long BytesPerMb = 1_000_000;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTime At, EvomiUsageRead Read)? _last;

    public async Task<EvomiUsageRead> GetAsync(DateTime nowUtc, CancellationToken ct)
    {
        var key = configuration[ApiKeySetting];
        if (string.IsNullOrWhiteSpace(key))
            return new EvomiUsageRead(null, false);

        await _lock.WaitAsync(ct);
        try
        {
            if (_last is not { } last || nowUtc - last.At >= MaxAge)
            {
                EvomiUsageRead read;
                try
                {
                    read = new EvomiUsageRead(await ReadAsync(key.Trim(), ct), false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Could not read the Evomi usage: {Error}", ex.Message);
                    read = new EvomiUsageRead(null, true);
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

    private async Task<EvomiUsage> ReadAsync(string key, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        using var account = await GetJsonAsync(http, key, "", ct);
        var balanceMb = Number(account.RootElement.GetProperty("products").GetProperty("rpc").GetProperty("balance_mb"));

        double? usedMb = null;
        using var usage = await GetJsonAsync(http, key, "/usage?product=rpc&period=7d", ct);
        if (usage.RootElement.TryGetProperty("meta", out var meta) && meta.TryGetProperty("total_bandwidth_mb", out var total))
            usedMb = Number(total);

        return new EvomiUsage((long)(balanceMb * BytesPerMb), usedMb is { } mb ? (long)(mb * BytesPerMb) : null);
    }

    /// <summary>A number, or a number sent as a string.</summary>
    private static double Number(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? double.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : e.GetDouble();

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string key, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Add("x-apikey", key);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GET /public{path.Split('?')[0]} answered {(int)response.StatusCode}");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(body, cancellationToken: ct);
    }
}
