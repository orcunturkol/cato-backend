using Cato.Domain.Entities;
using Cato.Infrastructure.Jobs;
using Cato.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Cato.API.Tests.JobRuns;

public class SecretAndStateStoreTests : JobRunsDbTestBase
{
    private IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task Worker_state_keeps_a_value_across_scopes_and_overwrites_it()
    {
        var scopes = Scopes();

        Assert.Null(await WorkerStateStore.GetAsync(scopes, WorkerStateKeys.PicsWatcherChangeNumber, default));
        await WorkerStateStore.SetAsync(scopes, WorkerStateKeys.PicsWatcherChangeNumber, "31337001", default);
        await WorkerStateStore.SetAsync(scopes, WorkerStateKeys.PicsWatcherChangeNumber, "31337099", default);

        Assert.Equal("31337099", await WorkerStateStore.GetAsync(scopes, WorkerStateKeys.PicsWatcherChangeNumber, default));
        using var db = NewContext();
        Assert.Single(db.WorkerStates);
    }

    [Fact]
    public async Task The_file_store_round_trips_a_secret_readable_by_its_owner_only()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cato-secrets-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSecretStore(dir);

            Assert.Null(await store.GetAsync(SecretIds.SteamKitAuth, default));
            await store.PutAsync(SecretIds.SteamKitAuth, """{"AccountName":"a","RefreshToken":"t"}""", default);

            Assert.Equal("""{"AccountName":"a","RefreshToken":"t"}""", await store.GetAsync(SecretIds.SteamKitAuth, default));
            var file = Assert.Single(Directory.GetFiles(dir));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
