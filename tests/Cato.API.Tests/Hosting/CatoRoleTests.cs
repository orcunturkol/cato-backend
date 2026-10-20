using Cato.API.Health;
using Cato.API.Hosting;
using Cato.Infrastructure.Redis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.Hosting;

public class CatoRoleTests
{
    [Theory]
    [InlineData(null, CatoRole.All)]
    [InlineData("", CatoRole.All)]
    [InlineData("all", CatoRole.All)]
    [InlineData(" API ", CatoRole.Api)]
    [InlineData("worker", CatoRole.Worker)]
    [InlineData("Migrate", CatoRole.Migrate)]
    public void Parses_the_role_and_defaults_to_all(string? value, CatoRole expected) =>
        Assert.Equal(expected, CatoRoles.Parse(value));

    [Fact]
    public void An_unknown_role_stops_the_start_with_a_clear_message()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CatoRoles.Parse("workers"));

        Assert.Contains("Use all, api, worker or migrate", error.Message);
    }

    [Theory]
    [InlineData(CatoRole.All, true, true)]
    [InlineData(CatoRole.Api, false, false)]
    [InlineData(CatoRole.Worker, true, false)]
    [InlineData(CatoRole.Migrate, false, false)]
    public void Only_all_and_worker_run_hosted_services_and_only_all_migrates_at_start(
        CatoRole role, bool hostedServices, bool migratesOnStart)
    {
        Assert.Equal(hostedServices, role.RunsHostedServices());
        Assert.Equal(migratesOnStart, role.MigratesOnStartup());
    }

    [Theory]
    [InlineData(CatoRole.All, true, true)]
    [InlineData(CatoRole.Api, true, false)]
    [InlineData(CatoRole.Worker, false, true)]
    [InlineData(CatoRole.Migrate, false, false)]
    public void Worker_only_controllers_stay_out_of_the_api(CatoRole role, bool mapsPlain, bool mapsWorkerOnly)
    {
        Assert.Equal(mapsPlain, role.MapsController(typeof(PlainController)));
        Assert.Equal(mapsWorkerOnly, role.MapsController(typeof(SteamOnlyController)));
    }

    [Fact]
    public void The_worker_registers_every_background_service_in_the_old_start_order()
    {
        var services = new ServiceCollection().AddWorkerHostedServices();

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();

        Assert.Equal(13, hosted.Count);
        Assert.Equal(typeof(RedisBackfillHostedService), hosted[0].ImplementationType);
    }

    [Fact]
    public void The_redis_backfill_no_longer_holds_back_the_web_server() =>
        Assert.True(typeof(BackgroundService).IsAssignableFrom(typeof(RedisBackfillHostedService)));

    [Theory]
    [InlineData(true, new[] { "database", "redis", "steamkit" })]
    [InlineData(false, new[] { "database", "redis" })]
    public void The_steamkit_check_exists_only_where_steamkit_runs(bool includeSteamKit, string[] expected)
    {
        using var provider = new ServiceCollection()
            .AddCatoHealthChecks(includeSteamKit)
            .BuildServiceProvider();

        var names = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(r => r.Name);

        Assert.Equal(expected, names);
    }

    private sealed class PlainController : ControllerBase;

    [WorkerOnly]
    private sealed class SteamOnlyController : ControllerBase;
}
