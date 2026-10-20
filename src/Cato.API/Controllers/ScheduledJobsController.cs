using Cato.API.Models.ScheduledJobs;
using Cato.API.Services.Handlers.ScheduledJobs;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

/// <summary>
/// Collector job schedules for the Jobs page. Times are UTC. A job is editable only after
/// its agent group moved from the host crontab to the job dispatcher.
/// </summary>
[ApiController]
[Route("api/scheduled-jobs")]
[Tags("Job Runs")]
public class ScheduledJobsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ScheduledJobsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Every collector job with its schedule, limits, alert threshold and next five runs.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ScheduledJobDto>), StatusCodes.Status200OK)]
    public async Task<IResult> List(CancellationToken ct) =>
        Results.Ok(await _mediator.Send(new GetScheduledJobsQuery(), ct));

    /// <summary>Save a schedule edit. The answer lists overlaps with other jobs of the same agent.</summary>
    [HttpPut("{name}")]
    [ProducesResponseType(typeof(ScheduledJobUpdateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IResult> Update(string name, [FromBody] UpdateScheduledJobRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new UpdateScheduledJobCommand(name, request), ct);
        if (result.IsSuccess) return Results.Ok(result.Data);
        return result.ErrorMessage == UpdateScheduledJobHandler.NotFound
            ? Results.NotFound()
            : Results.UnprocessableEntity(new { error = result.ErrorMessage });
    }

    /// <summary>Check a schedule without saving it: errors, overlaps and the next five runs.</summary>
    [HttpPost("{name}/preview")]
    [ProducesResponseType(typeof(SchedulePreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IResult> Preview(string name, [FromBody] PreviewScheduleRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new PreviewScheduleQuery(name, request), ct);
        return result.IsSuccess ? Results.Ok(result.Data) : Results.NotFound();
    }

    /// <summary>The change log of one job, newest first.</summary>
    [HttpGet("{name}/changes")]
    [ProducesResponseType(typeof(List<ScheduledJobChangeDto>), StatusCodes.Status200OK)]
    public async Task<IResult> Changes(string name, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Results.Ok(await _mediator.Send(new GetScheduledJobChangesQuery(name, limit), ct));
}
