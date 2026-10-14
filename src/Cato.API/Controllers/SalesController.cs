using Cato.API.Models.Ingestion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/sales")]
[Tags("Game Data")]
public class SalesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SalesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Lifetime and last-30-day Steam sales for every game with sales data.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(SalesSummaryDto), StatusCodes.Status200OK)]
    public async Task<IResult> GetSummary(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSalesSummaryQuery(), ct);
        return Results.Ok(result);
    }
}
