using Cato.API.Models.Ingestion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/nav-stats")]
[Tags("Game Data")]
public class NavStatsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NavStatsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Steamworks Nav Stats: store page traffic of every product over a date range. No publisherId (or 0) = all organizations, each product counted once; an id = that publisher alone.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NavStatsDto), StatusCodes.Status200OK)]
    public async Task<IResult> Get(
        [FromQuery] int? publisherId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNavStatsQuery(publisherId, from, to), ct);
        return Results.Ok(result);
    }
}
