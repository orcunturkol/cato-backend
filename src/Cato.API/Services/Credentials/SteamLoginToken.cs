using System.Text;
using System.Text.Json;

namespace Cato.API.Services.Credentials;

/// <summary>
/// The public facts of a Steam login token cookie ("&lt;steamid&gt;||&lt;JWT&gt;", URL-encoded,
/// or a bare JWT): account, audience and lifetime. Only the JWT's payload is decoded and the
/// signature is not checked; the token itself is never kept. Mirrors the collector's
/// <c>login_token_info</c>.
/// </summary>
public sealed record SteamLoginToken(string? Account, IReadOnlyList<string> Audience, DateTime IssuedAt, DateTime ExpiresAt)
{
    public TimeSpan Lifetime => ExpiresAt - IssuedAt;

    public static SteamLoginToken? TryParse(string? cookieValue)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
            return null;
        var value = Uri.UnescapeDataString(cookieValue.Trim());
        var separator = value.IndexOf("||", StringComparison.Ordinal);
        var jwt = separator >= 0 ? value[(separator + 2)..] : value;
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += new string('=', (4 - payload.Length % 4) % 4);
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var claims = doc.RootElement;
            var issued = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("iat").GetInt64()).UtcDateTime;
            var expires = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64()).UtcDateTime;
            string? account = claims.TryGetProperty("sub", out var sub) ? sub.ToString() : null;
            IReadOnlyList<string> audience = claims.TryGetProperty("aud", out var aud)
                ? aud.ValueKind == JsonValueKind.Array
                    ? aud.EnumerateArray().Select(a => a.ToString()).ToList()
                    : [aud.ToString()]
                : [];
            return new SteamLoginToken(account, audience, issued, expires);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException
                                       or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>"…" and the last four digits: enough to recognise the account, not to use it.</summary>
    public static string? Mask(string? account) =>
        string.IsNullOrEmpty(account) ? null : account.Length <= 4 ? "…" + account : "…" + account[^4..];
}

/// <summary>Cookies from what a person pastes; the same rules as the collector's parse_cookie_input.</summary>
public static class CookieInput
{
    /// <summary>
    /// A DevTools cookie header ("a=1; b=2"), with or without "Cookie:", or a bare value of
    /// <paramref name="bareName"/>. A header pasted without its first name gives the first
    /// value to <paramref name="bareName"/>. Values are kept exactly as pasted.
    /// </summary>
    public static Dictionary<string, string> Parse(string raw, string bareName)
    {
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = raw.Trim();
        if (text.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
            text = text["cookie:".Length..].Trim();
        if (text.Length == 0)
            return cookies;

        var first = text.Split(';', 2);
        if (!first[0].Contains('='))
        {
            cookies[bareName] = first[0].Trim();
            text = first.Length > 1 ? first[1] : "";
        }
        foreach (var part in text.Split(';'))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim().Length > 0)
                cookies[pair[0].Trim()] = pair[1].Trim();
        }
        return cookies;
    }
}
