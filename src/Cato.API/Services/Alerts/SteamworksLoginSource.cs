using Cato.API.Services.Credentials;

namespace Cato.API.Services.Alerts;

/// <summary>One read of the stored Steamworks login: Token null means none stored or unreadable.</summary>
public sealed record SteamworksLoginRead(SteamLoginToken? Token, bool Failed);

/// <summary>The stored Steamworks refresh login for the steamworks_login_expiring rule.</summary>
public interface ISteamworksLoginSource
{
    /// <summary>The last read, at most <see cref="SteamworksLoginCache.MaxAge"/> old.</summary>
    Task<SteamworksLoginRead> GetAsync(DateTime nowUtc, CancellationToken ct);
}

public sealed class CachedSteamworksLogin(SteamworksLoginService service, SteamworksLoginCache cache) : ISteamworksLoginSource
{
    public Task<SteamworksLoginRead> GetAsync(DateTime nowUtc, CancellationToken ct) => cache.GetAsync(service, nowUtc, ct);
}

/// <summary>
/// The last read of the refresh secret (singleton): the alert cycle runs every minute, the
/// token changes a few times a year. A failed read is kept too, so a store that cannot be
/// read is tried (and logged) once per <see cref="MaxAge"/>, not every cycle.
/// </summary>
public sealed class SteamworksLoginCache(ILogger<SteamworksLoginCache> logger)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTime At, SteamworksLoginRead Read)? _last;

    public async Task<SteamworksLoginRead> GetAsync(SteamworksLoginService service, DateTime nowUtc, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_last is not { } last || nowUtc - last.At >= MaxAge)
            {
                SteamworksLoginRead read;
                try
                {
                    read = new SteamworksLoginRead(await service.ReadRefreshTokenAsync(ct), false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Alert cycle could not read the Steamworks login: {Error}", ex.Message);
                    read = new SteamworksLoginRead(null, true);
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
}
