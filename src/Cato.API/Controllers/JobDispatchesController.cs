using Cato.API.Services.Dispatch;
using Cato.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Controllers;

public record FinishDispatchRequest(string AgentId, int ExitCode, string? Note);

public record JobDispatchDto(
    Guid Id, string JobName, string AgentGroup, DateTime ScheduledAt, string Kind, string? RequestedBy,
    string Status, string? ClaimedBy, DateTime? ClaimedAt, DateTime? FinishedAt, int? ExitCode, string? Note);

/// <summary>Runs the dispatcher handed out: recent history, and the agent's finish report.</summary>
[ApiController]
[Route("api/job-dispatches")]
[Tags("Job Runs")]
public class JobDispatchesController : ControllerBase
{
    private readonly JobDispatchService _dispatch;
    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public JobDispatchesController(JobDispatchService dispatch, CatoDbContext db, TimeProvider time)
    {
        _dispatch = dispatch;
        _db = db;
        _time = time;
    }

    /// <summary>The agent reports that a run ended, with its exit code (124 or 137: time limit).</summary>
    [HttpPost("{id:guid}/finish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IResult> Finish(Guid id, [FromBody] FinishDispatchRequest request, CancellationToken ct)
    {
        var note = request.Note is { Length: > 500 } long_ ? long_[..500] : request.Note;
        var outcome = await _dispatch.FinishAsync(id, request.AgentId, request.ExitCode, note, _time.GetUtcNow().UtcDateTime, ct);
        return outcome switch
        {
            FinishOutcome.Recorded => Results.NoContent(),
            FinishOutcome.NotFound => Results.NotFound(),
            _ => Results.Conflict("This dispatch was claimed by another agent."),
        };
    }

    /// <summary>Recent dispatches, newest first; filter by job name.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<JobDispatchDto>), StatusCodes.Status200OK)]
    public async Task<IResult> List([FromQuery] string? jobName, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var query = _db.JobDispatches.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(jobName)) query = query.Where(d => d.JobName == jobName);
        var rows = await query.OrderByDescending(d => d.ScheduledAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);
        return Results.Ok(rows.Select(d => new JobDispatchDto(
            d.Id, d.JobName, d.AgentGroup, Utc(d.ScheduledAt), d.Kind, d.RequestedBy, d.Status, d.ClaimedBy,
            d.ClaimedAt is { } c ? Utc(c) : null, d.FinishedAt is { } f ? Utc(f) : null, d.ExitCode, d.Note)).ToList());
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
