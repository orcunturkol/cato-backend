using Cato.API.Services.Dispatch;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Controllers;

public record AgentRequest(string AgentId, string? Host);

public record AgentHeartbeatRequest(string AgentId, string? Host, Guid? DispatchId);

public record JobAgentDto(
    string AgentId, string AgentGroup, string? Host, DateTime FirstSeenAt, DateTime LastSeenAt,
    Guid? CurrentDispatchId, string? CurrentJob);

/// <summary>
/// The job agents' side of the dispatcher: claim the next run of a group, and say
/// "still alive" while a run takes long. Groups: light, events, steamdb, steamworks.
/// </summary>
[ApiController]
[Route("api/job-agents")]
[Tags("Job Runs")]
public class JobAgentsController : ControllerBase
{
    private readonly JobDispatchService _dispatch;
    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public JobAgentsController(JobDispatchService dispatch, CatoDbContext db, TimeProvider time)
    {
        _dispatch = dispatch;
        _db = db;
        _time = time;
    }

    /// <summary>The oldest waiting run of the group, or 204 when there is none or the group is busy.</summary>
    [HttpPost("{group}/claim")]
    [ProducesResponseType(typeof(ClaimedRun), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> Claim(string group, [FromBody] AgentRequest request, CancellationToken ct)
    {
        if (Invalid(group, request.AgentId) is { } problem) return problem;
        var run = await _dispatch.ClaimAsync(group, request.AgentId, request.Host, _time.GetUtcNow().UtcDateTime, ct);
        return run is null ? Results.NoContent() : Results.Ok(run);
    }

    [HttpPost("{group}/heartbeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> Heartbeat(string group, [FromBody] AgentHeartbeatRequest request, CancellationToken ct)
    {
        if (Invalid(group, request.AgentId) is { } problem) return problem;
        await _dispatch.HeartbeatAsync(group, request.AgentId, request.Host, request.DispatchId, _time.GetUtcNow().UtcDateTime, ct);
        return Results.NoContent();
    }

    /// <summary>Every agent the API has heard from, with its last sign of life and current run.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<JobAgentDto>), StatusCodes.Status200OK)]
    public async Task<IResult> List(CancellationToken ct)
    {
        var agents = await _db.JobAgents.AsNoTracking().OrderBy(a => a.AgentGroup).ThenBy(a => a.AgentId).ToListAsync(ct);
        var dispatchIds = agents.Where(a => a.CurrentDispatchId is not null).Select(a => a.CurrentDispatchId!.Value).ToList();
        var jobs = await _db.JobDispatches.AsNoTracking()
            .Where(d => dispatchIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.JobName, ct);
        return Results.Ok(agents.Select(a => new JobAgentDto(
            a.AgentId, a.AgentGroup, a.Host,
            DateTime.SpecifyKind(a.FirstSeenAt, DateTimeKind.Utc), DateTime.SpecifyKind(a.LastSeenAt, DateTimeKind.Utc),
            a.CurrentDispatchId,
            a.CurrentDispatchId is { } id ? jobs.GetValueOrDefault(id) : null)).ToList());
    }

    private static IResult? Invalid(string group, string? agentId)
    {
        if (!JobAgentGroups.All.Contains(group))
            return Results.BadRequest($"Unknown agent group '{group}'. Use {string.Join(", ", JobAgentGroups.All)}.");
        if (string.IsNullOrWhiteSpace(agentId) || agentId.Length > 150)
            return Results.BadRequest("agentId is required (at most 150 characters).");
        return null;
    }
}
