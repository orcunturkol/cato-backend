using Cato.API.Models.JobRuns;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cato.API.Controllers;

[ApiController]
[Route("api/job-schedules")]
[Tags("Job Runs")]
public class JobSchedulesController : ControllerBase
{
    private readonly IMediator _mediator;

    public JobSchedulesController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Replace the schedule an external producer runs on (the collector sends its parsed
    /// crontab). Cron expressions are UTC.
    /// </summary>
    [HttpPut("{producer}")]
    [ProducesResponseType(typeof(JobScheduleSourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> Publish(
        string producer,
        [FromBody] PublishJobScheduleRequest request,
        [FromServices] IValidator<PublishJobScheduleCommand> validator,
        CancellationToken ct)
    {
        var command = new PublishJobScheduleCommand(
            producer,
            request.Source ?? string.Empty,
            request.SourceHost,
            request.Entries ?? []);

        var validation = await validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Results.BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        return Results.Ok(await _mediator.Send(command, ct));
    }
}
