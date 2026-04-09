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

    /// <summary>Ingest peak CCU history from an uploaded JSON file.</summary>
    [HttpPost("peak-ccu")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> IngestPeakCcu([FromForm] int appId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest("File is required.");
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new IngestPeakCcuCommand(appId, file.FileName, stream), ct);
        return Results.Ok(result);
    }

    /// <summary>Ingest regional sales history from an uploaded JSON file.</summary>
    [HttpPost("financial")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> IngestFinancialData([FromForm] int appId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest("File is required.");
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new IngestFinancialDataCommand(appId, file.FileName, stream), ct);
        return Results.Ok(result);
    }

    /// <summary>Ingest Steamworks wishlist CSV data from an uploaded file.</summary>
    [HttpPost("wishlists")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> IngestWishlistData([FromForm] int appId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest("File is required.");
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new IngestWishlistDataCommand(appId, file.FileName, stream), ct);
        return Results.Ok(result);
    }

    /// <summary>Ingest owned game wishlist/activation data from an uploaded JSON file.</summary>
    [HttpPost("owned-game")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> IngestOwnedGameData([FromForm] int appId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest("File is required.");
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new IngestOwnedGameDataCommand(appId, file.FileName, stream), ct);
        return Results.Ok(result);
    }

    /// <summary>Ingest regional price history from an uploaded JSON file (per-currency, multi-snapshot).</summary>
    [HttpPost("regional-prices")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> IngestRegionalPrices([FromForm] int appId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest("File is required.");
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new IngestRegionalPricesCommand(appId, file.FileName, stream), ct);
        return Results.Ok(result);
    }

    /// <summary>Ingest wishlist insights (also-wishlisted related games) from an uploaded JSON file.</summary>
    [HttpPost("wishlist-insights")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> IngestWishlistInsights([FromForm] int appId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest("File is required.");
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new IngestWishlistInsightsCommand(appId, file.FileName, stream), ct);
        return Results.Ok(result);
    }

    /// <summary>Ingest store traffic breakdown from an uploaded Steamworks CSV file (per-feature impressions/visits).</summary>
    [HttpPost("store-traffic")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(IngestionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> IngestStoreTraffic([FromForm] int appId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest("File is required.");
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new IngestStoreTrafficCommand(appId, file.FileName, stream), ct);
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
