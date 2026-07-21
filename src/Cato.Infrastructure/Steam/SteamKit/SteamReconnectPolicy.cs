using SteamKit2;

namespace Cato.Infrastructure.Steam.SteamKit;

/// <summary>
/// Decides whether and when SteamKitService may reconnect after a disconnect.
/// Single-flight: only one reconnect may be pending at a time. Backoff is based
/// on disconnect frequency (not auth failures), so a login-then-kick loop still
/// backs off. Session-conflict/rate-limit logoffs impose a longer cooldown.
/// </summary>
public sealed class SteamReconnectPolicy
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ChurnWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LogOffCooldown = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _recentDisconnects = new();

    private bool _reconnectPending;
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;

    public SteamReconnectPolicy(TimeProvider? timeProvider = null)
        => _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Called on every disconnect. Returns true if the caller owns scheduling a
    /// reconnect after <paramref name="delay"/>; false if one is already pending.
    /// </summary>
    public bool TryBeginReconnect(out TimeSpan delay)
    {
        lock (_gate)
        {
            if (_reconnectPending)
            {
                delay = default;
                return false;
            }

            var now = _time.GetUtcNow();
            while (_recentDisconnects.Count > 0 && now - _recentDisconnects.Peek() > ChurnWindow)
                _recentDisconnects.Dequeue();
            _recentDisconnects.Enqueue(now);

            var backoffSeconds = BaseDelay.TotalSeconds * Math.Pow(2, _recentDisconnects.Count - 1);
            delay = TimeSpan.FromSeconds(Math.Min(backoffSeconds, MaxDelay.TotalSeconds));

            if (_cooldownUntil - now > delay)
                delay = _cooldownUntil - now;

            _reconnectPending = true;
            return true;
        }
    }

    /// <summary>Called when the scheduled reconnect actually executes, releasing the gate.</summary>
    public void CompleteReconnect()
    {
        lock (_gate)
        {
            _reconnectPending = false;
        }
    }

    /// <summary>
    /// Records why Steam logged us off. Session conflicts and rate limits mean an
    /// eager retry only perpetuates the kicking, so they impose a cooldown.
    /// </summary>
    public void NoteLogOff(EResult result)
    {
        if (result is not (EResult.LoggedInElsewhere or EResult.LogonSessionReplaced
            or EResult.RateLimitExceeded or EResult.AccountLoginDeniedThrottle))
            return;

        lock (_gate)
        {
            var until = _time.GetUtcNow() + LogOffCooldown;
            if (until > _cooldownUntil)
                _cooldownUntil = until;
        }
    }
}
