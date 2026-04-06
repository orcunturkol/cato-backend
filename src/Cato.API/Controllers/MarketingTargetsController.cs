using Cato.API.DTOs;
using Cato.API.Models.MarketingTargets;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/marketing-targets")]
[Tags("Marketing Targets")]
public class MarketingTargetsController : ControllerBase
{
    private readonly IMediator _mediator;

    public MarketingTargetsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Create a new marketing target (influencer, event, mailing list, etc.).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MarketingTargetDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateMarketingTarget(
        [FromBody] CreateMarketingTargetCommand command,
        [FromServices] IValidator<CreateMarketingTargetCommand> validator)
    {
        var validation = await validator.ValidateAsync(command);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(command);
        return result.IsSuccess
            ? Results.Created($"/api/marketing-targets/{result.Data!.Id}", result.Data)
            : Results.BadRequest(result.ErrorMessage);
    }

    /// <summary>List marketing targets with optional filtering and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<MarketingTargetSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IResult> ListMarketingTargets(
        [FromQuery] string? targetType,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var result = await _mediator.Send(new ListMarketingTargetsQuery(targetType, search, page ?? 1, pageSize ?? 20));
        return Results.Ok(result);
    }

    /// <summary>Get a marketing target by Id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MarketingTargetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> GetMarketingTarget(Guid id)
    {
        var result = await _mediator.Send(new GetMarketingTargetQuery(id));
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.NotFound(result.ErrorMessage);
    }

    /// <summary>Partially update a marketing target.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(MarketingTargetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> UpdateMarketingTarget(
        Guid id,
        [FromBody] UpdateMarketingTargetCommand command,
        [FromServices] IValidator<UpdateMarketingTargetCommand> validator)
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

    /// <summary>Delete a marketing target.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteMarketingTarget(Guid id)
    {
        var result = await _mediator.Send(new DeleteMarketingTargetCommand(id));
        return result.IsSuccess
            ? Results.NoContent()
            : Results.NotFound(result.ErrorMessage);
    }
}
