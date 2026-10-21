using Cato.API.Hosting;
using Cato.API.Services.Enrichment;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

/// <summary>
/// The worker's side of <see cref="RemoteGameEnrichmentGateway"/>: the api role posts here.
/// Worker-only, so the api role does not map it and calls itself never.
/// </summary>
[ApiController]
[WorkerOnly]
[Route("api/internal/enrichment")]
[ApiExplorerSettings(IgnoreApi = true)]
public class InternalEnrichmentController : ControllerBase
{
    private readonly LocalGameEnrichmentGateway _enrichment;

    public InternalEnrichmentController(LocalGameEnrichmentGateway enrichment) => _enrichment = enrichment;

    [HttpPost("games/{id:guid}")]
    public async Task<IResult> Enrich(Guid id, CancellationToken ct) =>
        await _enrichment.EnrichAsync(id, ct) switch
        {
            EnrichmentOutcome.Enriched => Results.Ok(new { enriched = true }),
            EnrichmentOutcome.NotFound => Results.NotFound(),
            _ => Results.BadRequest(new { error = $"Enrichment failed for game {id}." }),
        };
}
