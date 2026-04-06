using Cato.API.DTOs;
using Cato.API.Models.Actions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/actions")]
[Tags("Marketing Actions")]
public class ActionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ActionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Create a new marketing action. Optionally link games and targets in the same request.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MarketingActionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateAction(
        [FromBody] CreateActionCommand command,
        [FromServices] IValidator<CreateActionCommand> validator)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(command);
        return result.IsSuccess
            ? Results.Created($"/api/actions/{result.Data!.Id}", result.Data)
            : Results.BadRequest(result.ErrorMessage);
    }

    /// <summary>List marketing actions with optional filtering and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<MarketingActionSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IResult> ListActions(
        [FromQuery] string? actionType,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var result = await _mediator.Send(new ListActionsQuery(actionType, status, search, page ?? 1, pageSize ?? 20));
        return Results.Ok(result);
    }

    /// <summary>Get a marketing action by Id, including linked games, targets, and impact.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MarketingActionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetAction(Guid id)
    {
        var result = await _mediator.Send(new GetActionQuery(id));
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Partially update a marketing action.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(MarketingActionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> UpdateAction(
        Guid id,
        [FromBody] UpdateActionCommand command,
        [FromServices] IValidator<UpdateActionCommand> validator)
    {
        var cmd = command with { Id = id };
        var validation = await validator.ValidateAsync(cmd);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(cmd);
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Delete a marketing action and all related data.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteAction(Guid id)
    {
        var result = await _mediator.Send(new DeleteActionCommand(id));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Link a game to a marketing action.</summary>
    [HttpPost("{id:guid}/games")]
    [ProducesResponseType(typeof(GameActionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> AddGame(
        Guid id,
        [FromBody] AddGameToActionCommand command,
        [FromServices] IValidator<AddGameToActionCommand> validator)
    {
        var cmd = command with { ActionId = id };
        var validation = await validator.ValidateAsync(cmd);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(cmd);
        return result.IsSuccess
            ? Results.Created($"/api/actions/{id}/games/{result.Data!.GameId}", result.Data)
            : Results.BadRequest(result.ErrorMessage);
    }

    /// <summary>Remove a game from a marketing action.</summary>
    [HttpDelete("{actionId:guid}/games/{gameId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> RemoveGame(Guid actionId, Guid gameId)
    {
        var result = await _mediator.Send(new RemoveGameFromActionCommand(actionId, gameId));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Link a marketing target to an action.</summary>
    [HttpPost("{id:guid}/targets")]
    [ProducesResponseType(typeof(ActionTargetDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> AddTarget(
        Guid id,
        [FromBody] AddTargetToActionCommand command,
        [FromServices] IValidator<AddTargetToActionCommand> validator)
    {
        var cmd = command with { ActionId = id };
        var validation = await validator.ValidateAsync(cmd);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(cmd);
        return result.IsSuccess
            ? Results.Created($"/api/actions/{id}/targets/{result.Data!.TargetId}", result.Data)
            : Results.BadRequest(result.ErrorMessage);
    }

    /// <summary>Remove a marketing target from an action.</summary>
    [HttpDelete("{actionId:guid}/targets/{targetId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> RemoveTarget(Guid actionId, Guid targetId)
    {
        var result = await _mediator.Send(new RemoveTargetFromActionCommand(actionId, targetId));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Update outreach status, views, engagement, cost for an action-target link.</summary>
    [HttpPatch("{actionId:guid}/targets/{targetId:guid}")]
    [ProducesResponseType(typeof(ActionTargetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> UpdateTarget(
        Guid actionId,
        Guid targetId,
        [FromBody] UpdateActionTargetCommand command,
        [FromServices] IValidator<UpdateActionTargetCommand> validator)
    {
        var cmd = command with { ActionId = actionId, TargetId = targetId };
        var validation = await validator.ValidateAsync(cmd);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(cmd);
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Upsert the impact measurement for a marketing action.</summary>
    [HttpPut("{id:guid}/impact")]
    [ProducesResponseType(typeof(ActionImpactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> UpsertImpact(Guid id, [FromBody] UpsertActionImpactCommand command)
    {
        var cmd = command with { ActionId = id };
        var result = await _mediator.Send(cmd);
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Get the impact measurement for a marketing action.</summary>
    [HttpGet("{id:guid}/impact")]
    [ProducesResponseType(typeof(ActionImpactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetImpact(Guid id)
    {
        var result = await _mediator.Send(new GetActionImpactQuery(id));
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.NotFound(result.ErrorMessage);
    }
}
