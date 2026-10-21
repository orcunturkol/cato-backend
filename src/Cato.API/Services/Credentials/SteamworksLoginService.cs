using System.Text.Json;
using Cato.API.DTOs;
using Cato.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

namespace Cato.API.Services.Credentials;

public sealed record SteamworksTokenStatus(
    string? Account, IReadOnlyList<string> Audience, DateTime IssuedAt, DateTime ExpiresAt, double DaysLeft);

/// <param name="Refresh">The long login (steamRefresh_partner, ~207 days) the jobs mint sessions from.</param>
/// <param name="Session">The last session token (steamLoginSecure, 24 h); its IssuedAt is the last refresh.</param>
public sealed record SteamworksLoginStatus(
    string Store, SteamworksTokenStatus? Refresh, SteamworksTokenStatus? Session, List<string> Warnings);

public sealed record SteamworksLoginUpdate(SteamworksLoginStatus Status, string Replaced, List<string> Warnings);

/// <summary>
/// The Steamworks login of the collector's jobs, in the secret store (Secrets Manager on the
/// server). Shows only public token facts and takes a new refresh cookie; never returns or
/// logs a cookie value. Replaces <c>run_steamworks_wishlist --set-refresh-cookie</c>.
/// </summary>
public sealed class SteamworksLoginService
{
    public const string RefreshCookie = "steamRefresh_partner";
    public const string RefreshCookiePrefix = "steamRefresh_";
    public const string SessionCookie = "steamLoginSecure";
    // A session token lives a day; a refresh token months. Below this it is the wrong cookie.
    private static readonly TimeSpan MinRefreshLifetime = TimeSpan.FromDays(7);
    private const double WarnDaysLeft = 14;

    private readonly ISecretStore _store;
    private readonly SecretStoreSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<SteamworksLoginService> _logger;

    public SteamworksLoginService(
        ISecretStore store, IOptions<SecretStoreSettings> settings, TimeProvider time, ILogger<SteamworksLoginService> logger)
    {
        _store = store;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<SteamworksLoginStatus> GetStatusAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var refreshCookies = await ReadCookiesAsync(SecretIds.SteamworksRefresh, ct);
        var refresh = RefreshName(refreshCookies) is { } name ? SteamLoginToken.TryParse(refreshCookies[name]) : null;
        var sessionCookies = await ReadCookiesAsync(SecretIds.SteamworksCookies, ct);
        var session = SteamLoginToken.TryParse(sessionCookies.GetValueOrDefault(SessionCookie));

        var warnings = new List<string>();
        if (refresh is null)
            warnings.Add("No Steamworks login is stored: the Steamworks jobs fail with auth_required until one is saved.");
        else if (refresh.ExpiresAt <= now)
            warnings.Add($"The Steamworks login expired on {refresh.ExpiresAt:yyyy-MM-dd}. Save a new one.");
        else if ((refresh.ExpiresAt - now).TotalDays < WarnDaysLeft)
            warnings.Add($"The Steamworks login expires in {(refresh.ExpiresAt - now).TotalDays:0} days. Save a new one soon.");
        if (refresh is not null && session is not null && refresh.Account != session.Account)
            warnings.Add("The last session belongs to another account than the stored login; the next run uses the stored login.");

        return new SteamworksLoginStatus(_settings.Backend, Describe(refresh, now), Describe(session, now), warnings);
    }

    /// <summary>Checks a pasted cookie (header or bare value) and stores it as the new refresh login.</summary>
    public async Task<Result<SteamworksLoginUpdate>> UpdateRefreshAsync(string? raw, string? changedBy, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var pasted = CookieInput.Parse(raw ?? "", RefreshCookie);
        if (RefreshName(pasted) is not { } name)
            return Result<SteamworksLoginUpdate>.Failure($"There is no {RefreshCookie} value in the input. Nothing was saved.");
        var token = SteamLoginToken.TryParse(pasted[name]);
        if (token is null)
            return Result<SteamworksLoginUpdate>.Failure("The value is not a Steam login token. Nothing was saved.");
        if (token.Lifetime < MinRefreshLifetime)
            return Result<SteamworksLoginUpdate>.Failure(
                $"This token lives under a week: it is a {SessionCookie} cookie, not {RefreshCookie}. Nothing was saved.");
        if (token.ExpiresAt <= now)
            return Result<SteamworksLoginUpdate>.Failure($"This login expired on {token.ExpiresAt:yyyy-MM-dd}. Nothing was saved.");

        var warnings = new List<string>();
        foreach (var other in pasted.Keys.Where(k => k != name && k.StartsWith(RefreshCookiePrefix, StringComparison.Ordinal)))
        {
            var otherToken = SteamLoginToken.TryParse(pasted[other]);
            // 2026-10-14: a store-only sign-in left the new account's steamRefresh_steam beside
            // the old account's steamRefresh_partner.
            if (otherToken is not null && otherToken.Account != token.Account)
                warnings.Add($"The input also has {other} for account {SteamLoginToken.Mask(otherToken.Account)}; " +
                             $"only {name} logs in to the partner sites. To use that account, sign in with it on " +
                             "partner.steampowered.com and copy the header again.");
        }

        var stored = await ReadCookiesAsync(SecretIds.SteamworksRefresh, ct);
        var previous = RefreshName(stored) is { } storedName ? SteamLoginToken.TryParse(stored[storedName]) : null;
        var replaced = previous is null
            ? "There was no stored login."
            : previous.Account == token.Account
                ? "It replaces the login of the same account."
                : $"It replaces the login of account {SteamLoginToken.Mask(previous.Account)}.";

        await _store.PutAsync(SecretIds.SteamworksRefresh,
            JsonSerializer.Serialize(new Dictionary<string, string> { [name] = pasted[name] }), ct);
        _logger.LogInformation(
            "Steamworks login updated by {ChangedBy}: account {Account}, expires {ExpiresAt:yyyy-MM-dd}",
            changedBy ?? "unknown", SteamLoginToken.Mask(token.Account), token.ExpiresAt);

        return Result<SteamworksLoginUpdate>.Success(new SteamworksLoginUpdate(await GetStatusAsync(ct), replaced, warnings));
    }

    private async Task<Dictionary<string, string>> ReadCookiesAsync(string id, CancellationToken ct)
    {
        var json = await _store.GetAsync(id, ct);
        if (json is null)
            return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // Never echo the content: it is a credential.
            throw new InvalidOperationException($"{id} is not a JSON object of cookies.");
        }
    }

    private static string? RefreshName(IReadOnlyDictionary<string, string> cookies) =>
        cookies.ContainsKey(RefreshCookie)
            ? RefreshCookie
            : cookies.Keys.FirstOrDefault(k => k.StartsWith(RefreshCookiePrefix, StringComparison.Ordinal));

    private static SteamworksTokenStatus? Describe(SteamLoginToken? token, DateTime now) =>
        token is null
            ? null
            : new SteamworksTokenStatus(SteamLoginToken.Mask(token.Account), token.Audience,
                DateTime.SpecifyKind(token.IssuedAt, DateTimeKind.Utc), DateTime.SpecifyKind(token.ExpiresAt, DateTimeKind.Utc),
                Math.Round((token.ExpiresAt - now).TotalDays, 1));
}
