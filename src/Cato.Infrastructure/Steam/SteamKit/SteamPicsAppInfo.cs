namespace Cato.Infrastructure.Steam.SteamKit;

/// <summary>
/// Lightweight representation of a Steam app's public metadata from PICS.
/// </summary>
public record SteamPicsAppInfo(
    uint AppId,
    string Name,
    string Type,
    string ReleaseState,
    DateOnly? ReleaseDate
);
