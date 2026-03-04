using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/ingestion")]
[Tags("Ingestion")]
public class IngestionController : ControllerBase
{
    private readonly IMediator _mediator;

    public IngestionController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Ingest peak CCU history from JSON file.</summary>
    [HttpPost("peak-ccu")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    public async Task<IResult> IngestPeakCcu([FromBody] IngestPeakCcuCommand command)
    {
        var result = await _mediator.Send(command);
        return Results.Ok(result);
    }

    /// <summary>Ingest regional sales history from JSON file.</summary>
    [HttpPost("financial")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    public async Task<IResult> IngestFinancialData([FromBody] IngestFinancialDataCommand command)
    {
        var result = await _mediator.Send(command);
        return Results.Ok(result);
    }

    /// <summary>Ingest Steamworks wishlist CSV data.</summary>
    [HttpPost("wishlists")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    public async Task<IResult> IngestWishlistData([FromBody] IngestWishlistDataCommand command)
    {
        var result = await _mediator.Send(command);
        return Results.Ok(result);
    }

    /// <summary>Ingest owned game wishlist/activation data from JSON.</summary>
    [HttpPost("owned-game")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    public async Task<IResult> IngestOwnedGameData([FromBody] IngestOwnedGameDataCommand command)
    {
        var result = await _mediator.Send(command);
        return Results.Ok(result);
    }

    /// <summary>Query ingestion logs for monitoring pipeline health.</summary>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(List<IngestionLogDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetIngestionLogs(
        [FromQuery] string? source,
        [FromQuery] int? limit)
    {
        var result = await _mediator.Send(new GetIngestionLogsQuery(source, limit ?? 20));
        return Results.Ok(result);
    }
}
