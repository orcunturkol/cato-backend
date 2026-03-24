using Cato.API.DTOs;
using Cato.API.Models.AppHistory;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/games/{gameId:guid}")]
[Tags("App History")]
public class AppHistoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public AppHistoryController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// List changelist summaries for a game, ordered by most recent first.
    /// Shows the changelist number, when it was detected, how many changes it contains,
    /// and which sections were affected.
    /// </summary>
    [HttpGet("changelists")]
    [ProducesResponseType(typeof(PagedResult<ChangelistSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetChangelists(
        Guid gameId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var result = await _mediator.Send(new GetChangelistsQuery(gameId, page ?? 1, pageSize ?? 20));
        return Results.Ok(result);
    }

    /// <summary>
    /// Get all change records for a specific changelist of a game.
    /// Returns individual key-value diffs showing old and new values.
    /// </summary>
    [HttpGet("changelists/{changeNumber:long}")]
    [ProducesResponseType(typeof(List<AppChangeRecordDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetChangelistDetail(
        Guid gameId,
        long changeNumber)
    {
        var result = await _mediator.Send(new GetChangelistDetailQuery(gameId, changeNumber));
        return Results.Ok(result);
    }

    /// <summary>
    /// Get paginated change history for a game with optional section filter.
    /// Section values: common, depots, config, extended, ufs
    /// </summary>
    [HttpGet("change-history")]
    [ProducesResponseType(typeof(PagedResult<AppChangeRecordDto>), StatusCodes.Status200OK)]
    public async Task<IResult> GetChangeHistory(
        Guid gameId,
        [FromQuery] string? section,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var result = await _mediator.Send(new GetChangeHistoryQuery(gameId, section, page ?? 1, pageSize ?? 50));
        return Results.Ok(result);
    }
}
