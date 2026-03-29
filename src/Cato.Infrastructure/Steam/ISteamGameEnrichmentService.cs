namespace Cato.Infrastructure.Steam;

public interface ISteamGameEnrichmentService
{
    /// <summary>
    /// Enriches a game entity with data from the Steam Store API (descriptions, images,
    /// price, platforms, developer/publisher, genres, tags, etc.).
    /// </summary>
    /// <returns>True if enrichment succeeded, false otherwise.</returns>
    Task<bool> EnrichGameAsync(Guid gameId, CancellationToken ct = default);
}
