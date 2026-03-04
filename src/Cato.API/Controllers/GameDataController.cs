using Cato.API.Models.Ingestion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/games/{appId}")]
[Tags("Game Data")]
public class GameDataController : ControllerBase
{
    private readonly IMediator _mediator;

    public GameDataController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Get peak CCU history for a game.</summary>
    [HttpGet("ccu")]
    [ProducesResponseType(typeof(List<CcuDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetCcuHistory(
        int appId,
        [FromQuery] string? source,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetCcuHistoryQuery(appId, source, limit ?? 100));
        return Results.Ok(result);
    }

    /// <summary>Get financial/sales data for a game.</summary>
    [HttpGet("financials")]
    [ProducesResponseType(typeof(List<FinancialDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetFinancialData(
        int appId,
        [FromQuery] string? countryCode,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetFinancialDataQuery(appId, countryCode, limit ?? 100));
        return Results.Ok(result);
    }

    /// <summary>Get wishlist/traffic data for a game.</summary>
    [HttpGet("traffic")]
    [ProducesResponseType(typeof(List<TrafficDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetTrafficData(
        int appId,
        [FromQuery] string? source,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetTrafficDataQuery(appId, source, limit ?? 100));
        return Results.Ok(result);
    }

    /// <summary>Get owned game wishlist/activation snapshots.</summary>
    [HttpGet("owned-data")]
    [ProducesResponseType(typeof(List<OwnedGameDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetOwnedGameData(
        int appId,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetOwnedGameDataQuery(appId, limit ?? 30));
        return Results.Ok(result);
    }
}
