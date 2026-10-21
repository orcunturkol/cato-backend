using Cato.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;

namespace Cato.API.Tests.JobRuns;

public class SecretConfigurationTests
{
    private static ConfigurationManager Config()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SteamKit:Password"] = "from-appsettings",
            ["SteamKit:Username"] = "from-appsettings",
        });
        return config;
    }

    [Fact]
    public void Secret_keys_win_over_earlier_configuration()
    {
        var config = Config();
        var store = new FakeStore("""{"SteamKit:Password":"from-secret","SteamWebApi:ApiKey":"k"}""");

        var keys = config.AddCatoSecrets(store);

        Assert.Equal(["SteamKit:Password", "SteamWebApi:ApiKey"], keys);
        Assert.Equal("from-secret", config["SteamKit:Password"]);
        Assert.Equal("from-appsettings", config["SteamKit:Username"]);
        Assert.Equal(SecretConfiguration.BackendConfigId, store.LastId);
    }

    [Fact]
    public void A_secret_with_no_value_yet_changes_nothing()
    {
        var config = Config();

        Assert.Empty(config.AddCatoSecrets(new FakeStore(null)));
        Assert.Equal("from-appsettings", config["SteamKit:Password"]);
    }

    [Fact]
    public void The_file_backend_reads_no_secrets()
    {
        var config = Config();

        Assert.Empty(config.AddCatoSecrets());
    }

    [Fact]
    public void An_unreadable_store_stops_the_start()
    {
        Assert.Throws<InvalidOperationException>(() => Config().AddCatoSecrets(new FakeStore(throws: true)));
    }

    private sealed class FakeStore(string? value = null, bool throws = false) : ISecretStore
    {
        public string? LastId { get; private set; }

        public Task<string?> GetAsync(string id, CancellationToken ct)
        {
            LastId = id;
            return throws ? throw new InvalidOperationException("secrets unreachable") : Task.FromResult(value);
        }

        public Task PutAsync(string id, string value, CancellationToken ct) => Task.CompletedTask;
    }
}
