using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cato.API.Health;

/// <summary>
/// Two endpoints. <c>/health/live</c> runs no checks: it only proves the process serves HTTP,
/// so a short database blip never makes ECS or Docker replace a working task.
/// <c>/health/ready</c> checks the database, Redis and SteamKit, for people and alarms.
/// </summary>
public static class HealthEndpoints
{
    private const string ReadyTag = "ready";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static IServiceCollection AddCatoHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5))
            .AddCheck<RedisHealthCheck>("redis", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5))
            .AddCheck<SteamKitHealthCheck>("steamkit", HealthStatus.Degraded, [ReadyTag]);
        return services;
    }

    public static IEndpointRouteBuilder MapCatoHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteJsonAsync,
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteJsonAsync,
        });
        return app;
    }

    /// <summary>Status per check, without exception details: those stay in the logs.</summary>
    public static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            status = report.Status.ToString(),
            durationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = (long)entry.Value.Duration.TotalMilliseconds,
                }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
