using Cato.API.Models.Ingestion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/bundles")]
[Tags("Game Data")]
public class BundlesController : ControllerBase
{
    private readonly IMediator _mediator;

    public BundlesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Steamworks bundle sales per bundle over a date range (default: all time).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(BundlesDto), StatusCodes.Status200OK)]
    public async Task<IResult> GetBundles([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Results.Ok(await _mediator.Send(new GetBundlesQuery(from, to), ct));

    /// <summary>One bundle's daily sales and its packages over a date range.</summary>
    [HttpGet("{bundleId:int}")]
    [ProducesResponseType(typeof(BundleDetailDto), StatusCodes.Status200OK)]
    public async Task<IResult> GetBundle(int bundleId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Results.Ok(await _mediator.Send(new GetBundleQuery(bundleId, from, to), ct));
}
