namespace Cato.Infrastructure.Steam;

/// <summary>
/// Steam Web API key settings (api.steampowered.com keyed endpoints).
/// Kept separate from PlayerProfileSettings so future jobs (e.g. the
/// achievements watcher) can reuse the same key without coupling to
/// profile-watcher knobs. Supply via env var SteamWebApi__ApiKey —
/// never commit a real key.
/// </summary>
public class SteamWebApiSettings
{
    public const string SectionName = "SteamWebApi";

    public string ApiKey { get; set; } = string.Empty;
}
