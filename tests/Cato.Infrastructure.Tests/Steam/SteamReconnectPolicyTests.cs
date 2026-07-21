using Cato.Infrastructure.Steam.SteamKit;
using SteamKit2;

namespace Cato.Infrastructure.Tests.Steam;

public class SteamReconnectPolicyTests
{
    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private readonly FakeTime _time = new();

    private SteamReconnectPolicy CreatePolicy() => new(_time);

    /// Runs one full disconnect -> reconnect-scheduled -> reconnect-executed cycle.
    private static TimeSpan CompleteCycle(SteamReconnectPolicy policy)
    {
        Assert.True(policy.TryBeginReconnect(out var delay));
        policy.CompleteReconnect();
        return delay;
    }

    [Fact]
    public void FirstDisconnect_SchedulesBaseDelay()
    {
        var policy = CreatePolicy();

        var scheduled = policy.TryBeginReconnect(out var delay);

        Assert.True(scheduled);
        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }

    [Fact]
    public void WhileReconnectPending_FurtherDisconnectsAreSwallowed()
    {
        var policy = CreatePolicy();
        Assert.True(policy.TryBeginReconnect(out _));

        // A second disconnect event arrives before the pending reconnect ran
        // (this is exactly what multiplied the reconnect loops in production).
        var scheduled = policy.TryBeginReconnect(out _);

        Assert.False(scheduled);
    }

    [Fact]
    public void AfterCompleteReconnect_NextDisconnectSchedulesAgain()
    {
        var policy = CreatePolicy();
        CompleteCycle(policy);

        var scheduled = policy.TryBeginReconnect(out _);

        Assert.True(scheduled);
    }

    [Fact]
    public void RapidDisconnects_BackoffDoubles()
    {
        var policy = CreatePolicy();

        var first = CompleteCycle(policy);
        _time.Advance(TimeSpan.FromSeconds(15));
        var second = CompleteCycle(policy);
        _time.Advance(TimeSpan.FromSeconds(15));
        var third = CompleteCycle(policy);

        Assert.Equal(TimeSpan.FromSeconds(10), first);
        Assert.Equal(TimeSpan.FromSeconds(20), second);
        Assert.Equal(TimeSpan.FromSeconds(40), third);
    }

    [Fact]
    public void RapidDisconnects_BackoffIsCappedAtFiveMinutes()
    {
        var policy = CreatePolicy();

        var delay = TimeSpan.Zero;
        for (var i = 0; i < 12; i++)
        {
            delay = CompleteCycle(policy);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(TimeSpan.FromMinutes(5), delay);
    }

    [Fact]
    public void DisconnectsSpacedBeyondWindow_KeepBaseDelay()
    {
        var policy = CreatePolicy();

        CompleteCycle(policy);
        _time.Advance(TimeSpan.FromMinutes(6)); // beyond the 5-minute churn window
        var delay = CompleteCycle(policy);

        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }

    [Fact]
    public void SessionConflictLogOff_ImposesCooldown()
    {
        var policy = CreatePolicy();

        policy.NoteLogOff(EResult.LoggedInElsewhere);
        policy.TryBeginReconnect(out var delay);

        Assert.Equal(TimeSpan.FromMinutes(10), delay);
    }

    [Fact]
    public void RateLimitLogOff_ImposesCooldown()
    {
        var policy = CreatePolicy();

        policy.NoteLogOff(EResult.RateLimitExceeded);
        policy.TryBeginReconnect(out var delay);

        Assert.Equal(TimeSpan.FromMinutes(10), delay);
    }

    [Fact]
    public void OrdinaryLogOff_DoesNotImposeCooldown()
    {
        var policy = CreatePolicy();

        policy.NoteLogOff(EResult.ServiceUnavailable);
        policy.TryBeginReconnect(out var delay);

        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }

    [Fact]
    public void CooldownExpires_BackToFrequencyBasedDelay()
    {
        var policy = CreatePolicy();

        policy.NoteLogOff(EResult.LoggedInElsewhere);
        _time.Advance(TimeSpan.FromMinutes(11));
        policy.TryBeginReconnect(out var delay);

        Assert.Equal(TimeSpan.FromSeconds(10), delay);
    }
}
