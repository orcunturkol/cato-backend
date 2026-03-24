namespace Cato.Infrastructure.Steam.SteamKit;

/// <summary>
/// Full raw PICS product info for an app, including the complete KeyValue tree
/// serialized as nested dictionaries and the per-app change number.
/// </summary>
public record SteamPicsRawAppInfo(
    uint AppId,
    uint ChangeNumber,
    Dictionary<string, object?> KeyValues);
