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

    /// <summary>Report a completed orchestrator/collector run (external producers).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(JobRunDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> Report(
        [FromBody] ReportJobRunCommand command,
        [FromServices] IValidator<ReportJobRunCommand> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var result = await _mediator.Send(command, ct);
        return Results.Ok(result);
    }

    /// <summary>List recent job runs for monitoring (filter by jobName/status).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<JobRunDto>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromQuery] string? jobName,
        [FromQuery] string? status,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetJobRunsQuery(jobName, status, limit ?? 50), ct);
        return Results.Ok(result);
    }
}
