using Microsoft.Extensions.Logging;
using SteamKit2.Authentication;

namespace Cato.Infrastructure.Steam.SteamKit;

/// <summary>
/// SteamKit2 <see cref="IAuthenticator"/> for an unattended background service.
/// There is no operator present to type in a code, so any interactive prompt
/// is logged clearly and surfaced as a failure rather than left to hang.
/// </summary>
internal sealed class HeadlessSteamAuthenticator(ILogger logger) : IAuthenticator
{
    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
    {
        logger.LogError("SteamKit2: Steam requested a Mobile Authenticator code, but this is an unattended service with no way to supply one.");
        throw new InvalidOperationException("Steam requested a two-factor authenticator code, which this headless service cannot provide.");
    }

    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
    {
        logger.LogError("SteamKit2: Steam requested an email Steam Guard code sent to {Email}, but this is an unattended service with no way to supply one.", email);
        throw new InvalidOperationException("Steam requested an email Steam Guard code, which this headless service cannot provide.");
    }

    public Task<bool> AcceptDeviceConfirmationAsync()
    {
        logger.LogWarning("SteamKit2: Steam requires confirmation via the Steam Mobile App to complete this login.");
        return Task.FromResult(true);
    }
}
