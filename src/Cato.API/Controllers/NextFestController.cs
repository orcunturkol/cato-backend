using Cato.API.Models.Ingestion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/next-fest")]
[Tags("Game Data")]
public class NextFestController : ControllerBase
{
    private readonly IMediator _mediator;

    public NextFestController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Steam Next Fest recaps of the publisher's games (demo players and wishlists during each fest).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NextFestDto), StatusCodes.Status200OK)]
    public async Task<IResult> Get(CancellationToken ct) => Results.Ok(await _mediator.Send(new GetNextFestQuery(), ct));
}
