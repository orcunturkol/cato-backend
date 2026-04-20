namespace Cato.Infrastructure.Redis;

/// <summary>
/// Maintains the Redis sorted sets the Python orchestrators read from.
/// Filter policy matches the legacy <c>db_game_provider.py</c>:
/// only GameType IN ('Sourcing', 'Owned') are tracked.
/// </summary>
public interface IRedisAppIdSyncService
{
    /// <summary>Add an appid to all tracked sets (ZADD NX, score=0) and
    /// record its name for orchestrator lookup. No-op when gameType is
    /// not 'Sourcing' or 'Owned'.</summary>
    Task SyncAsync(int appId, string gameType, string? name, CancellationToken ct);

    /// <summary>Apply a GameType transition. Moves the appid in or out of the
    /// tracked sets based on old and new type.</summary>
    Task UpdateAsync(int appId, string oldType, string newType, string? name, CancellationToken ct);

    /// <summary>Remove the appid from every tracked set and the name hash.</summary>
    Task RemoveAsync(int appId, CancellationToken ct);
}
