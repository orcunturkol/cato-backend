using Cato.API.DTOs;
using Cato.API.Models.SteamDb;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/steamdb-rankings")]
[Tags("SteamDB Rankings")]
public class SteamDbRankingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SteamDbRankingsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Get cross-game SteamDB rankings for a given source and date.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SteamDbRankingDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetRankings(
        [FromQuery] string source,
        [FromQuery] DateOnly? date,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetSteamDbRankingsQuery(source, date, search, page, pageSize));
        return Results.Ok(result);
    }

    /// <summary>Get available snapshot dates for a given source.</summary>
    [HttpGet("available-dates")]
    [ProducesResponseType(typeof(List<DateOnly>), StatusCodes.Status200OK)]
    public async Task<IResult> GetAvailableDates([FromQuery] string source)
    {
        var result = await _mediator.Send(new GetAvailableDatesQuery(source));
        return Results.Ok(result);
    }
}
