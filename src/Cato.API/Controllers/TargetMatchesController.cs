using Cato.API.DTOs;
using Cato.API.Models.Actions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/target-matches")]
[Tags("Target Matches")]
public class TargetMatchesController : ControllerBase
{
    private readonly IMediator _mediator;

    public TargetMatchesController(IMediator mediator) => _mediator = mediator;

    /// <summary>List target match scores, optionally filtered by game, target, or lifecycle stage.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TargetMatchDto>), StatusCodes.Status200OK)]
    public async Task<IResult> ListTargetMatches(
        [FromQuery] Guid? gameId,
        [FromQuery] Guid? targetId,
        [FromQuery] string? lifecycleStage,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var result = await _mediator.Send(new ListTargetMatchesQuery(gameId, targetId, lifecycleStage, page ?? 1, pageSize ?? 20));
        return Results.Ok(result);
    }

    /// <summary>Upsert a target match score for a game + target + lifecycle stage combination.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(TargetMatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> UpsertTargetMatch(
        [FromBody] UpsertTargetMatchCommand command,
        [FromServices] IValidator<UpsertTargetMatchCommand> validator)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(command);
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.BadRequest(result.ErrorMessage);
    }
}
