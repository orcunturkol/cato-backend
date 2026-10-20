using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Cato.API.Health;

/// <summary>Ready check: the database accepts a connection.</summary>
public sealed class DatabaseHealthCheck(CatoDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database refused the connection");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database unreachable", ex);
        }
    }
}

/// <summary>Ready check: Redis is connected and answers a ping.</summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!redis.IsConnected)
            return HealthCheckResult.Unhealthy("redis not connected");
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"redis ping {latency.TotalMilliseconds:F0} ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("redis ping failed", ex);
        }
    }
}

/// <summary>
/// SteamKit is Degraded, never Unhealthy, while logged out: every restart starts with a
/// fresh Steam login and the service reconnects on its own. An orchestrator that replaced
/// tasks on this check would cause the very logouts it reports.
/// </summary>
public sealed class SteamKitHealthCheck(ISteamKitService steamKit) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(steamKit.IsConnected
            ? HealthCheckResult.Healthy("steamkit logged in")
            : HealthCheckResult.Degraded("steamkit not logged in; it reconnects on its own"));
}
