using Cato.API.Models.JobRuns;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/job-runs")]
[Tags("Job Runs")]
public class JobRunsController : ControllerBase
{
    private readonly IMediator _mediator;

    public JobRunsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Report a run from an external producer. Send <c>id</c> to report "Running" at
    /// start and the outcome at finish for the same row.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(JobRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IResult> Report(
        [FromBody] ReportJobRunCommand command,
        [FromServices] IValidator<ReportJobRunCommand> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Results.Ok(result.Data)
            : Results.Conflict(result.ErrorMessage);
    }

    /// <summary>List recent job runs (filter by jobName, producer, stored status).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<JobRunDto>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromQuery] string? jobName,
        [FromQuery] string? status,
        [FromQuery] int? limit,
        [FromQuery] string? producer,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetJobRunsQuery(jobName, status, limit ?? 50, producer), ct);
        return Results.Ok(result);
    }

    /// <summary>Runs in progress, and every job with its schedule, last run and next run.</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(JobRunsOverviewDto), StatusCodes.Status200OK)]
    public async Task<IResult> Overview([FromQuery] int? recentRuns, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetJobRunsOverviewQuery(recentRuns ?? 5), ct);
        return Results.Ok(result);
    }
}
