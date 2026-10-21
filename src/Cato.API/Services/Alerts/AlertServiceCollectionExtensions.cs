using Amazon;
using Amazon.CloudWatch;
using Amazon.SecretsManager;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Options;

namespace Cato.API.Services.Alerts;

public static class AlertServiceCollectionExtensions
{
    /// <summary>
    /// Alert services for every role (the API serves the test endpoint). A channel exists
    /// only when its setting is there. The AWS clients use the default credential chain:
    /// the EC2 instance role on the server.
    /// </summary>
    public static IServiceCollection AddAlerts(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AlertSettings.SectionName);
        services.Configure<AlertSettings>(section);
        var settings = section.Get<AlertSettings>() ?? new AlertSettings();
        var region = RegionEndpoint.GetBySystemName(settings.AwsRegion);

        if (!string.IsNullOrWhiteSpace(settings.SnsTopicArn))
        {
            services.AddSingleton<IAmazonSimpleNotificationService>(_ => new AmazonSimpleNotificationServiceClient(region));
            services.AddSingleton<IAlertNotifier, SnsAlertNotifier>();
        }

        if (!string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl) || !string.IsNullOrWhiteSpace(settings.DiscordWebhookSecretId))
        {
            if (string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
                services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient(region));
            services.AddHttpClient(nameof(DiscordAlertNotifier), c => c.Timeout = TimeSpan.FromSeconds(10));
            services.AddSingleton<IAlertNotifier>(sp => new DiscordAlertNotifier(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(DiscordAlertNotifier)),
                sp.GetRequiredService<IOptions<AlertSettings>>(),
                sp.GetService<IAmazonSecretsManager>()));
        }

        if (settings.HeartbeatEnabled)
            services.AddSingleton<IAmazonCloudWatch>(_ => new AmazonCloudWatchClient(region));

        services.AddSingleton<IQueueStatsReader, RabbitQueueStatsReader>();
        services.AddSingleton<QueueProgressTracker>();
        services.AddScoped<Cato.API.Services.JobRuns.JobFreshnessService>();
        services.AddSingleton<Cato.API.Services.JobRuns.JobFreshnessCache>();
        services.AddScoped<Cato.API.Services.JobRuns.IJobFreshnessSource, Cato.API.Services.JobRuns.CachedJobFreshness>();
        services.AddScoped<AlertService>();
        return services;
    }
}
