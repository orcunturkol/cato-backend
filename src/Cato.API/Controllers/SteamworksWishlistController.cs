using Cato.API.Models.Ingestion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/steamworks-wishlist")]
[Tags("Game Data")]
public class SteamworksWishlistController : ControllerBase
{
    private readonly IMediator _mediator;

    public SteamworksWishlistController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Lifetime and last-30-day Steamworks wishlist totals for every game with report data.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(SteamworksWishlistSummaryDto), StatusCodes.Status200OK)]
    public async Task<IResult> GetSummary(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSteamworksWishlistSummaryQuery(), ct);
        return Results.Ok(result);
    }
}
