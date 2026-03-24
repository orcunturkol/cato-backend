using Cato.API.DTOs;
using Cato.API.Models.AppHistory;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/recent-events")]
[Tags("Recent Events")]
public class RecentEventsController : ControllerBase
{
    private readonly IMediator _mediator;

    public RecentEventsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Get recent PICS change events across all tracked apps.
    /// Optionally filter by game type (Owned, Competitor, Sourcing).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RecentAppEventDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetRecentEvents(
        [FromQuery] string? gameType,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var result = await _mediator.Send(new GetRecentAppEventsQuery(gameType, page ?? 1, pageSize ?? 50));
        return Results.Ok(result);
    }
}
