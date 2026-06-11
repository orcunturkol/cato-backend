namespace Cato.Infrastructure.Redis;

/// <summary>
/// Rotation queue for reviewer SteamIDs awaiting profile fetches
/// (GetPlayerSummaries). Backed by a Redis sorted set where the score is the
/// last successful fetch as a unix timestamp (0 = never fetched), mirroring
/// the Python orchestrators' app-id rotation pattern.
/// </summary>
public interface ISteamIdRotationService
{
    /// <summary>
    /// Adds steamids to the rotation with score 0, only if not already present
    /// (ZADD NX). Best-effort: Redis errors are logged and swallowed — seeding
    /// must never break review ingestion.
    /// </summary>
    Task SeedAsync(IReadOnlyCollection<long> steamIds, CancellationToken ct);

    /// <summary>Returns the N members with the oldest scores (never-fetched first).</summary>
    Task<IReadOnlyList<long>> FetchBatchAsync(int n, CancellationToken ct);

    /// <summary>
    /// Updates the member's score to the fetch time (ZADD XX — a quarantined
    /// id removed from the set is not resurrected) and clears its failure count.
    /// </summary>
    Task MarkSuccessAsync(long steamId, DateTime fetchedAtUtc, CancellationToken ct);

    /// <summary>
    /// Increments the member's failure count; at the threshold the id is moved
    /// to the quarantine set and removed from rotation. Returns true if quarantined.
    /// </summary>
    Task<bool> MarkFailureAsync(long steamId, string reason, CancellationToken ct);
}
