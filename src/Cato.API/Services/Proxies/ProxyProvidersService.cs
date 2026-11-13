using Cato.API.Services.Alerts;

namespace Cato.API.Services.Proxies;

public sealed record ProxyProvidersDto(
    DateTime GeneratedAt, int Days, ProxyFallback? Fallback, List<ProxyProviderDto> Providers);

/// <param name="Order">Place in the collector's PROXY_PROVIDERS order (1 = first choice); null when not listed.</param>
/// <param name="SuccessRate">Ok / (ok + failed) over the days, 0-1; null without requests.</param>
public sealed record ProxyProviderDto(
    string Name, int? Order, bool Configured, ProxyDownFlag? Down, ProxyUsageDto Usage,
    long Ok, long Failed, double? SuccessRate,
    List<ProxyCountDto> FailReasons, List<ProxyTargetDto> Targets, List<ProxyDayDto> Daily);

/// <param name="UsedBytes">Traffic in <paramref name="UsedPeriod"/>.</param>
/// <param name="LeftBytes">What can still be used (plan left or prepaid balance).</param>
/// <param name="Note">Why a number is missing, or where to read it.</param>
public sealed record ProxyUsageDto(
    long? UsedBytes, string? UsedPeriod, long? LimitBytes, long? LeftBytes, DateTime? RenewsAt, string? Note);

public sealed record ProxyCountDto(string Reason, long Count);

public sealed record ProxyTargetDto(string Target, long Ok, long Failed, double? SuccessRate);

public sealed record ProxyDayDto(DateOnly Date, long Ok, long Failed);

/// <summary>
/// The Proxies page: per provider the collector's request counts (success rate, failure
/// reasons, targets), its down flag and the usage from the provider's API.
/// </summary>
public sealed class ProxyProvidersService(
    IProxyStateReader state, IWebshareUsageSource webshare, IEvomiUsageSource evomi)
{
    /// <summary>The providers the collector knows, in its default fallback order.</summary>
    public static readonly string[] KnownProviders = ["webshare", "evomi", "dataimpulse"];

    public async Task<ProxyProvidersDto> GetAsync(int days, DateTime nowUtc, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 40);
        var listed = await state.GetProvidersAsync();
        var stats = await state.GetStatsAsync(DateOnly.FromDateTime(nowUtc), days);
        var names = listed.Select(p => p.Name).Concat(KnownProviders).Concat(stats.Keys).Distinct().ToList();

        var providers = new List<ProxyProviderDto>();
        foreach (var name in names)
        {
            var index = listed.ToList().FindIndex(p => p.Name == name);
            var dayStats = stats.GetValueOrDefault(name) ?? [];
            long ok = dayStats.Sum(d => d.Ok), failed = dayStats.Sum(d => d.Failed);
            var reasons = dayStats.SelectMany(d => d.FailReasons)
                .GroupBy(r => r.Key).Select(g => new ProxyCountDto(g.Key, g.Sum(r => r.Value)))
                .OrderByDescending(r => r.Count).ToList();
            var targets = dayStats.SelectMany(d => d.Targets)
                .GroupBy(t => t.Key)
                .Select(g =>
                {
                    long tOk = g.Sum(t => t.Value.Ok), tFailed = g.Sum(t => t.Value.Failed);
                    return new ProxyTargetDto(g.Key, tOk, tFailed, Rate(tOk, tFailed));
                })
                .OrderByDescending(t => t.Ok + t.Failed).ToList();
            providers.Add(new ProxyProviderDto(
                name, index >= 0 ? index + 1 : null, index >= 0 && listed[index].Configured,
                await state.GetDownAsync(name), await UsageAsync(name, nowUtc, ct),
                ok, failed, Rate(ok, failed), reasons, targets,
                dayStats.Select(d => new ProxyDayDto(d.Date, d.Ok, d.Failed)).ToList()));
        }

        var ordered = providers.OrderBy(p => p.Order ?? int.MaxValue).ToList();
        return new ProxyProvidersDto(nowUtc, days, await state.GetFallbackAsync(), ordered);
    }

    private static double? Rate(long ok, long failed) => ok + failed == 0 ? null : (double)ok / (ok + failed);

    private async Task<ProxyUsageDto> UsageAsync(string name, DateTime nowUtc, CancellationToken ct)
    {
        switch (name)
        {
            case "webshare":
            {
                var read = await webshare.GetAsync(nowUtc, ct);
                if (read.Usage is { } u)
                    return new ProxyUsageDto(u.UsedBytes, $"since {u.PeriodStart:yyyy-MM-dd}", u.LimitBytes,
                        Math.Max(0, u.LimitBytes - u.UsedBytes), u.PeriodEnd, null);
                return Missing(read.Failed, "Webshare", WebshareUsageSource.ApiKeySetting);
            }
            case "evomi":
            {
                var read = await evomi.GetAsync(nowUtc, ct);
                if (read.Usage is { } u)
                    return new ProxyUsageDto(u.UsedLast7DaysBytes, "last 7 days", null, u.BalanceBytes, null,
                        "Prepaid balance; it does not expire.");
                return Missing(read.Failed, "Evomi", EvomiUsageSource.ApiKeySetting);
            }
            case "dataimpulse":
                return new ProxyUsageDto(null, null, null, null, null,
                    "DataImpulse has no documented usage API: see the dashboard on app.dataimpulse.com.");
            default:
                return new ProxyUsageDto(null, null, null, null, null, "No usage source for this provider.");
        }
    }

    private static ProxyUsageDto Missing(bool failed, string provider, string setting) =>
        new(null, null, null, null, null, failed
            ? $"The {provider} API could not be read; the next try is within 30 minutes."
            : $"No {provider} API key ({setting} in cato/backend/config).");
}
