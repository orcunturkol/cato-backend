using System.Reflection;
using Cato.Infrastructure.Messaging;
using Cato.Infrastructure.Redis;
using Cato.Infrastructure.Steam;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Cato.API.Hosting;

public static class RoleHostingExtensions
{
    /// <summary>
    /// Every background service, in the order they started before the split. Their settings
    /// and the singletons they use stay registered in every role; only these run in the worker.
    /// </summary>
    public static IServiceCollection AddWorkerHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<RedisBackfillHostedService>();
        services.AddHostedService<FollowerHistorySweepService>();
        services.AddHostedService<RabbitMqConsumerService>();
        services.AddHostedService<SteamPlayerProfileWatcherService>();
        services.AddHostedService<GameEnrichmentWatcherService>();
        services.AddHostedService<GameAchievementSchemaWatcherService>();
        services.AddHostedService<PlayerAchievementWatcherService>();
        services.AddHostedService<GameNewsWatcherService>();
        services.AddHostedService(sp => sp.GetRequiredService<SteamKitService>());
        services.AddHostedService<SteamPicsWatcherService>();
        services.AddHostedService<SteamPriceWatcherService>();
        services.AddHostedService<SteamPicsChangeHistoryService>();
        services.AddHostedService<SteamReviewWatcherService>();
        // Exactly one dispatcher: the worker is a single task.
        services.AddHostedService<Services.Dispatch.JobDispatcherService>();
        return services;
    }

    /// <summary>Maps only the controllers this role serves (see <see cref="CatoRoles.MapsController"/>).</summary>
    public static IMvcBuilder ForRole(this IMvcBuilder mvc, CatoRole role) =>
        mvc.ConfigureApplicationPartManager(manager =>
        {
            var defaults = manager.FeatureProviders.OfType<ControllerFeatureProvider>().ToList();
            foreach (var provider in defaults)
                manager.FeatureProviders.Remove(provider);
            manager.FeatureProviders.Add(new RoleControllerFeatureProvider(role));
        });

    private sealed class RoleControllerFeatureProvider(CatoRole role) : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) =>
            base.IsController(typeInfo) && role.MapsController(typeInfo.AsType());
    }
}
