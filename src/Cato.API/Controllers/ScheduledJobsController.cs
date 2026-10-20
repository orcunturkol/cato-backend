using Cato.API.Models.ScheduledJobs;
using Cato.API.Services.Dispatch;
using Cato.API.Services.Handlers.ScheduledJobs;
using Cato.Domain.Entities;
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

    /// <summary>Run the job now: its agent claims it within seconds.</summary>
    [HttpPost("{name}/run")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IResult> RunNow(
        string name, [FromBody] RunNowRequest? request,
        [FromServices] JobDispatchService dispatch, [FromServices] TimeProvider time, CancellationToken ct)
    {
        var result = await dispatch.RequestRunAsync(name, request?.RequestedBy ?? "jobs page", time.GetUtcNow().UtcDateTime, ct);
        return result.Outcome switch
        {
            RunRequestOutcome.Queued => Results.Accepted(null, new { dispatchId = result.DispatchId }),
            RunRequestOutcome.NotFound => Results.NotFound(),
            _ => Results.Conflict(new { error = result.Message, dispatchId = result.DispatchId }),
        };
    }

    /// <summary>
    /// Move an agent group's jobs between the host crontab ("cron") and the dispatcher
    /// ("dispatcher"). Start the group's agent and comment out its crontab lines in the same
    /// minute; moving back to cron expires the group's waiting dispatches.
    /// </summary>
    [HttpPost("groups/{group}/managed-by")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> SetManagedBy(
        string group, [FromBody] SetManagedByRequest request,
        [FromServices] JobDispatchService dispatch, [FromServices] TimeProvider time, CancellationToken ct)
    {
        if (!JobAgentGroups.All.Contains(group))
            return Results.BadRequest($"Unknown agent group '{group}'.");
        if (request.ManagedBy is not (ScheduledJobManagedBy.Cron or ScheduledJobManagedBy.Dispatcher))
            return Results.BadRequest("managedBy must be \"cron\" or \"dispatcher\".");
        var changed = await dispatch.SetGroupManagedByAsync(
            group, request.ManagedBy, request.ChangedBy, request.Reason, time.GetUtcNow().UtcDateTime, ct);
        return Results.Ok(changed);
    }
}

public record RunNowRequest(string? RequestedBy);

public record SetManagedByRequest(string ManagedBy, string? ChangedBy, string? Reason);
