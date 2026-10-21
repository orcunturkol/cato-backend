using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Cato.Infrastructure.Secrets;

/// <summary>
/// Static backend secrets (database connection, Steam account, Web API key) from one
/// Secrets Manager secret, as a JSON object of configuration keys:
/// <c>{"SteamKit:Password": "...", "ConnectionStrings:DefaultConnection": "..."}</c>.
/// Added last, so it wins over appsettings and environment variables.
/// </summary>
public static class SecretConfiguration
{
    public const string BackendConfigId = "cato/backend/config";

    /// <summary>
    /// With <c>Secrets:Backend = aws</c>, reads <see cref="BackendConfigId"/> once at start.
    /// A secret with no value yet changes nothing; any other error stops the start, because
    /// running with the wrong credentials is worse than not running (the heartbeat alarm
    /// reports a worker that does not start). Returns the keys it added, never the values.
    /// </summary>
    public static IReadOnlyList<string> AddCatoSecrets(this IConfigurationManager configuration, ISecretStore? store = null)
    {
        var settings = configuration.GetSection(SecretStoreSettings.SectionName).Get<SecretStoreSettings>()
                       ?? new SecretStoreSettings();
        if (store is null && settings.Backend != "aws")
            return [];
        store ??= AwsSecretStore.Create(settings);

        var json = store.GetAsync(BackendConfigId, CancellationToken.None).GetAwaiter().GetResult();
        if (json is null)
            return [];

        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                     ?? throw new InvalidOperationException($"{BackendConfigId} is not a JSON object.");
        configuration.AddInMemoryCollection(values);
        return values.Keys.Order(StringComparer.Ordinal).ToList();
    }
}
