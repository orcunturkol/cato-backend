using Cato.API.DTOs;
using Cato.API.Models.Games;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

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
