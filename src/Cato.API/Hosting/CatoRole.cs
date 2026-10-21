namespace Cato.API.Hosting;

/// <summary>
/// What this process does, from <c>CATO_ROLE</c>. One image runs as three ECS tasks:
/// <c>api</c> serves HTTP only, <c>worker</c> runs every hosted service (exactly one task,
/// because SteamKit is one Steam login and the Steam throttles are per process), and
/// <c>migrate</c> applies EF migrations once and exits. <c>all</c> (the default) is today's
/// single container on EC2, so nothing changes until a role is set.
/// </summary>
public enum CatoRole
{
    All,
    Api,
    Worker,
    Migrate,
}

public static class CatoRoles
{
    public const string ConfigurationKey = "CATO_ROLE";

    public static CatoRole Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "all" => CatoRole.All,
        "api" => CatoRole.Api,
        "worker" => CatoRole.Worker,
        "migrate" => CatoRole.Migrate,
        _ => throw new InvalidOperationException(
            $"Unknown {ConfigurationKey} '{value}'. Use all, api, worker or migrate."),
    };

    /// <summary>Hosted services, SteamKit and the queue consumers.</summary>
    public static bool RunsHostedServices(this CatoRole role) => role is CatoRole.All or CatoRole.Worker;

    /// <summary>Only the single-container setup migrates at startup; ECS runs the migrate task first.</summary>
    public static bool MigratesOnStartup(this CatoRole role) => role is CatoRole.All;

    /// <summary>
    /// Npgsql's Maximum Pool Size when <c>Database:MaxPoolSize</c> is not set. Npgsql's own
    /// default is 100 per process; RDS db.t4g.medium allows about 400 connections in all, so
    /// several api tasks and the worker must stay well below it. The single container uses about 6.
    /// </summary>
    public static int DefaultMaxPoolSize(this CatoRole role) => role switch
    {
        CatoRole.Api => 30,
        CatoRole.Worker => 40,
        CatoRole.Migrate => 5,
        _ => 60,
    };

    /// <summary>
    /// Which controllers a role maps. A <see cref="WorkerOnlyAttribute"/> controller needs the
    /// worker's SteamKit login, so the api role leaves it out and the worker maps only those.
    /// </summary>
    public static bool MapsController(this CatoRole role, Type controller)
    {
        var workerOnly = Attribute.IsDefined(controller, typeof(WorkerOnlyAttribute), inherit: true);
        return role switch
        {
            CatoRole.All => true,
            CatoRole.Api => !workerOnly,
            CatoRole.Worker => workerOnly,
            _ => false,
        };
    }
}

/// <summary>Marks a controller that needs the worker process (for example the SteamKit login).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class WorkerOnlyAttribute : Attribute;
