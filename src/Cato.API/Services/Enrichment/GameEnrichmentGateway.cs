using System.Net;
using Cato.Infrastructure.Steam;

namespace Cato.API.Services.Enrichment;

public enum EnrichmentOutcome
{
    Enriched,
    /// <summary>Steam had no usable store record, or the game does not exist.</summary>
    Failed,
    NotFound,
}

/// <summary>
/// Store enrichment of one game. Enrichment calls Steam through the throttled
/// SteamApiService, so it runs in the worker: in role all or worker this calls the
/// enrichment service directly; in role api it asks the worker over HTTP. Every path that
/// enriches (single enrich, by app id for OGT, re-enrich, bulk import, owned-sync) goes
/// through GameService and lands here.
/// </summary>
public interface IGameEnrichmentGateway
{
    Task<EnrichmentOutcome> EnrichAsync(Guid gameId, CancellationToken ct);
}

public sealed class LocalGameEnrichmentGateway(ISteamGameEnrichmentService enrichment) : IGameEnrichmentGateway
{
    public async Task<EnrichmentOutcome> EnrichAsync(Guid gameId, CancellationToken ct) =>
        await enrichment.EnrichGameAsync(gameId, ct) ? EnrichmentOutcome.Enriched : EnrichmentOutcome.Failed;
}

/// <summary>The api role's gateway: POST to the worker's InternalEnrichmentController.</summary>
public sealed class RemoteGameEnrichmentGateway(HttpClient http, ILogger<RemoteGameEnrichmentGateway> logger) : IGameEnrichmentGateway
{
    /// <summary>One enrichment waits on Steam's throttle; the store answers within seconds.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public async Task<EnrichmentOutcome> EnrichAsync(Guid gameId, CancellationToken ct)
    {
        using var response = await http.PostAsync($"api/internal/enrichment/games/{gameId}", content: null, ct);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return EnrichmentOutcome.Enriched;
            case HttpStatusCode.NotFound:
                return EnrichmentOutcome.NotFound;
            case HttpStatusCode.BadRequest:
                return EnrichmentOutcome.Failed;
            default:
                // The worker is down or failed: the caller reports it like a failed enrichment.
                logger.LogWarning("Worker enrichment of {GameId} answered {Status}", gameId, (int)response.StatusCode);
                throw new HttpRequestException($"worker enrichment answered {(int)response.StatusCode}", null, response.StatusCode);
        }
    }
}
