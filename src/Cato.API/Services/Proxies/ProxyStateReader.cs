using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;

namespace Cato.API.Services.Proxies;

/// <summary>A provider in the collector's PROXY_PROVIDERS order; Configured = its login is set.</summary>
public sealed record ProxyProviderEntry(string Name, bool Configured);

/// <summary>The provider refused all traffic (plan used up, login refused) since <c>Since</c>.</summary>
public sealed record ProxyDownFlag(string Reason, DateTime? Since, DateTime? LastSeen);

public sealed record ProxySkip(string Name, string Reason);

/// <summary>A job used <c>Provider</c> in place of the earlier ones; refreshed while it lasts.</summary>
public sealed record ProxyFallback(string Provider, IReadOnlyList<ProxySkip> Skipped, DateTime? At);

/// <summary>One provider's request counts on one UTC day.</summary>
public sealed record ProxyDayStats(
    DateOnly Date, long Ok, long Failed,
    IReadOnlyDictionary<string, long> FailReasons,
    IReadOnlyDictionary<string, (long Ok, long Failed)> Targets);

/// <summary>
/// The collector's proxy state in Redis (`proxy_providers/state.py` in the collector):
/// the provider order, down flags, the fallback flag and per-day request counts.
/// </summary>
public interface IProxyStateReader
{
    Task<IReadOnlyList<ProxyProviderEntry>> GetProvidersAsync();
    Task<ProxyDownFlag?> GetDownAsync(string provider);
    Task<ProxyFallback?> GetFallbackAsync();
    /// <summary>Counts by provider for each of the <paramref name="days"/> UTC days up to <paramref name="today"/>.</summary>
    Task<IReadOnlyDictionary<string, List<ProxyDayStats>>> GetStatsAsync(DateOnly today, int days);
}

public sealed class RedisProxyStateReader(IConnectionMultiplexer redis) : IProxyStateReader
{
    public const string Prefix = "cato:collector:proxy";
    public const string ProvidersKey = Prefix + ":providers";
    public const string FallbackKey = Prefix + ":fallback";
    public static string DownKey(string provider) => $"{Prefix}:down:{provider}";
    public static string StatsKey(DateOnly day) => $"{Prefix}:stats:{day:yyyy-MM-dd}";

    private IDatabase Db => redis.GetDatabase();

    public async Task<IReadOnlyList<ProxyProviderEntry>> GetProvidersAsync()
    {
        using var doc = Parse(await Db.StringGetAsync(ProvidersKey));
        if (doc?.RootElement.ValueKind != JsonValueKind.Array)
            return [];
        return doc.RootElement.EnumerateArray()
            .Select(e => new ProxyProviderEntry(Text(e, "name") ?? "", e.TryGetProperty("configured", out var c) && c.ValueKind == JsonValueKind.True))
            .Where(p => p.Name.Length > 0)
            .ToList();
    }

    public async Task<ProxyDownFlag?> GetDownAsync(string provider)
    {
        using var doc = Parse(await Db.StringGetAsync(DownKey(provider)));
        if (doc is null)
            return null;
        var root = doc.RootElement;
        return new ProxyDownFlag(Text(root, "reason") ?? "down", Time(root, "since"), Time(root, "last_seen"));
    }

    public async Task<ProxyFallback?> GetFallbackAsync()
    {
        using var doc = Parse(await Db.StringGetAsync(FallbackKey));
        if (doc is null)
            return null;
        var root = doc.RootElement;
        var skipped = root.TryGetProperty("skipped", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().Select(e => new ProxySkip(Text(e, "name") ?? "", Text(e, "reason") ?? "")).ToList()
            : [];
        return new ProxyFallback(Text(root, "provider") ?? "", skipped, Time(root, "at"));
    }

    public async Task<IReadOnlyDictionary<string, List<ProxyDayStats>>> GetStatsAsync(DateOnly today, int days)
    {
        var result = new Dictionary<string, List<ProxyDayStats>>();
        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var fields = await Db.HashGetAllAsync(StatsKey(day));
            foreach (var (provider, stats) in ParseDay(day, fields.Select(f => ((string)f.Name!, (long)f.Value))))
            {
                if (!result.TryGetValue(provider, out var list))
                    result[provider] = list = [];
                list.Add(stats);
            }
        }
        return result;
    }

    /// <summary>
    /// Hash fields of one day: <c>name|ok</c>, <c>name|fail</c>, <c>name|fail|reason</c>,
    /// <c>name|target|target|ok</c> and <c>|fail</c>.
    /// </summary>
    public static Dictionary<string, ProxyDayStats> ParseDay(DateOnly day, IEnumerable<(string Field, long Value)> fields)
    {
        var ok = new Dictionary<string, long>();
        var failed = new Dictionary<string, long>();
        var reasons = new Dictionary<string, Dictionary<string, long>>();
        var targets = new Dictionary<string, Dictionary<string, (long Ok, long Failed)>>();
        foreach (var (field, value) in fields)
        {
            var parts = field.Split('|');
            var name = parts[0];
            switch (parts)
            {
                case [_, "ok"]:
                    ok[name] = value;
                    break;
                case [_, "fail"]:
                    failed[name] = value;
                    break;
                case [_, "fail", var reason]:
                    Get(reasons, name)[reason] = value;
                    break;
                case [_, "target", var target, var outcome]:
                    var map = Get(targets, name);
                    var (o, f) = map.GetValueOrDefault(target);
                    map[target] = outcome == "ok" ? (value, f) : (o, value);
                    break;
                default:
                    continue;
            }
        }
        return ok.Keys.Union(failed.Keys).Union(reasons.Keys).Union(targets.Keys).ToDictionary(
            name => name,
            name => new ProxyDayStats(day, ok.GetValueOrDefault(name), failed.GetValueOrDefault(name),
                reasons.GetValueOrDefault(name) ?? [], targets.GetValueOrDefault(name) ?? []));
    }

    private static Dictionary<string, T> Get<T>(Dictionary<string, Dictionary<string, T>> map, string key)
    {
        if (!map.TryGetValue(key, out var value))
            map[key] = value = [];
        return value;
    }

    private static JsonDocument? Parse(RedisValue raw)
    {
        if (raw.IsNullOrEmpty)
            return null;
        try
        {
            return JsonDocument.Parse((string)raw!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? Time(JsonElement e, string name) =>
        DateTime.TryParse(Text(e, name), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
