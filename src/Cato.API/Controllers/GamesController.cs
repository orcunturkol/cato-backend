using Cato.API.DTOs;
using Cato.API.Models.Games;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/games")]
[Tags("Games")]
public class GamesController : ControllerBase
{
    private readonly IMediator _mediator;

    public GamesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Create a new game by AppId.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(GameDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateGame(
        [FromBody] CreateGameCommand command,
        [FromServices] IValidator<CreateGameCommand> validator)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(command);
        return result.IsSuccess
            ? Results.Created($"/api/games/{result.Data!.Id}", result.Data)
            : Results.BadRequest(result.ErrorMessage);
    }

    /// <summary>List all games with optional filtering and paging.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<GameDto>), StatusCodes.Status200OK)]
    public async Task<IResult> ListGames(
        [FromQuery] string? gameType,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var result = await _mediator.Send(new ListGamesQuery(gameType, search, page ?? 1, pageSize ?? 20));
        return Results.Ok(result);
    }

    /// <summary>Catalog feed for sibling services: filter by creation date and/or AppIds. Output-cached.</summary>
    [HttpGet("catalog")]
    [OutputCache(PolicyName = "GamesCatalog")]
    [ProducesResponseType(typeof(PagedResult<GameDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> CatalogGames(
        [FromQuery] DateTime? createdAfter,
        [FromQuery] string? appIds,
        [FromQuery] string? search,
        [FromQuery] string? developer,
        [FromQuery] string? publisher,
        [FromQuery] DateOnly? releasedAfter,
        [FromQuery] DateOnly? releasedBefore,
        [FromQuery] bool? hasTrailer,
        [FromQuery] string? gameType,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        List<int>? ids = null;
        if (!string.IsNullOrWhiteSpace(appIds))
        {
            ids = [];
            foreach (var part in appIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, out var id))
                    return Results.BadRequest(new { error = $"appIds contains a non-integer value: '{part}'." });
                ids.Add(id);
            }
        }

        var result = await _mediator.Send(new CatalogGamesQuery(
            createdAfter, ids, search, developer, publisher,
            releasedAfter, releasedBefore, hasTrailer,
            gameType, sortBy, sortDir,
            page ?? 1, pageSize ?? 100));
        return Results.Ok(result);
    }

    /// <summary>Enrich a game from Steam by its AppId (for sibling services keyed on AppId).</summary>
    [HttpPost("by-appid/{appId:int}/enrich")]
    [ProducesResponseType(typeof(GameDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> EnrichGameByAppId(int appId)
    {
        var result = await _mediator.Send(new EnrichGameByAppIdCommand(appId));
        if (result.IsSuccess)
            return Results.Ok(result.Data);
        return result.ErrorMessage!.Contains("not found")
            ? Results.NotFound(new { error = result.ErrorMessage })
            : Results.BadRequest(new { error = result.ErrorMessage });
    }

    /// <summary>Get details for a specific game by Id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GameDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetGameDetails(Guid id)
    {
        var result = await _mediator.Send(new GetGameDetailsQuery(id));
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Partially update a game's properties.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(GameDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> UpdateGame(
        Guid id,
        [FromBody] UpdateGameCommand command,
        [FromServices] IValidator<UpdateGameCommand> validator)
    {
        // Ensure route id matches body id
        var cmd = command with { Id = id };
        var validation = await validator.ValidateAsync(cmd);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(cmd);
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Delete a game and all related data.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteGame(Guid id)
    {
        var result = await _mediator.Send(new DeleteGameCommand(id));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Bulk import games from an uploaded CSV or XLSX file.
    /// Parses the file immediately (returns 400 if malformed) then publishes one event per AppId for async processing.</summary>
    [HttpPost("bulk-import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(BulkImportResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> BulkImportGames(IFormFile file, [FromForm] string? gameType, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest("File is required.");

        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new BulkImportGamesCommand(file.FileName, stream, gameType), ct);
        return result.IsSuccess
            ? Results.Accepted(null, result.Data)
            : Results.BadRequest(result.ErrorMessage);

    }

    /// <summary>Re-enrich all games where enrichment was not successful (HeaderImageUrl is null).
    /// Returns immediately with the count of queued games; enrichment runs in the background.</summary>
    [HttpPost("re-enrich")]
    [ProducesResponseType(typeof(ReEnrichAllGamesBackgroundResult), StatusCodes.Status202Accepted)]
    public async Task<IResult> ReEnrichAllGames()
    {
        var result = await _mediator.Send(new ReEnrichAllGamesBackgroundCommand());
        return Results.Accepted(null, result.Data);
    }

    /// <summary>Enrich a game with data from the Steam API.</summary>
    [HttpPost("{id:guid}/enrich")]
    [ProducesResponseType(typeof(GameDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> EnrichGameFromSteam(Guid id)
    {
        var result = await _mediator.Send(new EnrichGameFromSteamCommand(id));
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.BadRequest(result.ErrorMessage);
    }
}
