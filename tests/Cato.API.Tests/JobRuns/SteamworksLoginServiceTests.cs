using System.Text;
using System.Text.Json;
using Cato.API.Services.Credentials;
using Cato.Infrastructure.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.JobRuns;

public class SteamworksLoginServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 15, 21, 0, DateTimeKind.Utc);
    private const string Account = "76561198000001234";

    private readonly FakeStore _store = new();
    private readonly ListLogger _log = new();

    private SteamworksLoginService Service() => new(
        _store, Options.Create(new SecretStoreSettings { Backend = "aws" }), new FixedTime(Now), _log);

    /// <summary>"&lt;steamid&gt;%7C%7C&lt;JWT&gt;", as the partner site sets it; the signature is fake.</summary>
    private static string Token(string account, TimeSpan lifetime, DateTime? issued = null, string audience = "\"web\",\"renew\",\"derive\"")
    {
        var iat = new DateTimeOffset(issued ?? Now.AddDays(-1)).ToUnixTimeSeconds();
        var exp = iat + (long)lifetime.TotalSeconds;
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwt = $"{B64("{\"typ\":\"JWT\",\"alg\":\"EdDSA\"}")}.{B64($"{{\"sub\":\"{account}\",\"aud\":[{audience}],\"iat\":{iat},\"exp\":{exp}}}")}.c2lnbmF0dXJl";
        return $"{account}%7C%7C{jwt}";
    }

    [Fact]
    public async Task With_no_login_the_status_says_so()
    {
        var status = await Service().GetStatusAsync(default);

        Assert.Null(status.Refresh);
        Assert.Contains("No Steamworks login is stored", Assert.Single(status.Warnings));
    }

    [Fact]
    public async Task A_pasted_refresh_value_is_stored_and_shown_masked()
    {
        var value = Token(Account, TimeSpan.FromDays(207));

        var result = await Service().UpdateRefreshAsync(value, "orcun", default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal($"{{\"steamRefresh_partner\":\"{value}\"}}", _store.Values[SecretIds.SteamworksRefresh]);
        var refresh = result.Data!.Status.Refresh!;
        Assert.Equal("…1234", refresh.Account);
        Assert.Equal(206, refresh.DaysLeft);
        Assert.Equal("There was no stored login.", result.Data.Replaced);
        Assert.Empty(result.Data.Status.Warnings);
    }

    [Fact]
    public async Task A_cookie_header_gives_its_refresh_cookie_and_warns_about_a_second_account()
    {
        var refresh = Token(Account, TimeSpan.FromDays(207));
        var other = Token("76561198000009999", TimeSpan.FromDays(207));
        var header = $"Cookie: steamLoginSecure={Token(Account, TimeSpan.FromHours(24))}; steamRefresh_partner={refresh}; steamRefresh_steam={other}";

        var result = await Service().UpdateRefreshAsync(header, "orcun", default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(refresh, _store.Values[SecretIds.SteamworksRefresh]);
        Assert.DoesNotContain(other, _store.Values[SecretIds.SteamworksRefresh]);
        Assert.Contains("steamRefresh_steam for account …9999", Assert.Single(result.Data!.Warnings));
    }

    [Theory]
    [InlineData("session", "lives under a week")]
    [InlineData("expired", "expired on")]
    [InlineData("garbage", "not a Steam login token")]
    [InlineData("empty", "no steamRefresh_partner value")]
    public async Task A_wrong_value_is_refused_and_nothing_is_stored(string kind, string message)
    {
        var raw = kind switch
        {
            "session" => $"steamRefresh_partner={Token(Account, TimeSpan.FromHours(24))}",
            "expired" => Token(Account, TimeSpan.FromDays(207), issued: Now.AddDays(-300)),
            "garbage" => "not-a-token",
            _ => "   ",
        };

        var result = await Service().UpdateRefreshAsync(raw, "orcun", default);

        Assert.False(result.IsSuccess);
        Assert.Contains(message, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_store.Values);
    }

    [Fact]
    public async Task A_login_close_to_its_end_warns_and_the_last_session_shows_the_last_refresh()
    {
        _store.Values[SecretIds.SteamworksRefresh] = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["steamRefresh_partner"] = Token(Account, TimeSpan.FromDays(207), issued: Now.AddDays(-200)),
        });
        _store.Values[SecretIds.SteamworksCookies] = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["steamLoginSecure"] = Token(Account, TimeSpan.FromHours(24), issued: Now.AddHours(-5)),
        });

        var status = await Service().GetStatusAsync(default);

        Assert.Contains("expires in 7 days", Assert.Single(status.Warnings));
        Assert.Equal(Now.AddHours(-5), status.Session!.IssuedAt);
    }

    [Fact]
    public async Task No_cookie_value_reaches_the_response_or_the_log()
    {
        var value = Token(Account, TimeSpan.FromDays(207));
        var jwt = Uri.UnescapeDataString(value).Split("||")[1];

        var result = await Service().UpdateRefreshAsync(value, "orcun", default);
        var bad = await Service().UpdateRefreshAsync($"steamRefresh_partner={Token(Account, TimeSpan.FromHours(1))}", "orcun", default);

        var json = JsonSerializer.Serialize(result.Data) + JsonSerializer.Serialize(bad);
        Assert.DoesNotContain(jwt, json);
        Assert.DoesNotContain(jwt.Split('.')[1], json);
        Assert.All(_log.Messages, m => Assert.DoesNotContain(jwt.Split('.')[1], m));
        Assert.Contains(_log.Messages, m => m.Contains("…1234"));
    }

    private sealed class FakeStore : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public Task<string?> GetAsync(string id, CancellationToken ct) => Task.FromResult(Values.GetValueOrDefault(id));
        public Task PutAsync(string id, string value, CancellationToken ct) { Values[id] = value; return Task.CompletedTask; }
    }

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class ListLogger : ILogger<SteamworksLoginService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
