using Cato.API.Services.Alerts;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Controllers;

public record AlertIncidentDto(
    Guid Id, string JobName, string Rule, string Title, string Message, Guid? RunId,
    DateTime OpenedAt, DateTime LastSeenAt, DateTime? ResolvedAt, string OpenSentTo, string ResolveSentTo);

public record TestAlertRequest(string? RequestedBy);

/// <summary>Alert incidents (open first) and a test message through every channel.</summary>
[ApiController]
[Route("api/alerts")]
[Tags("Job Runs")]
public class AlertsController : ControllerBase
{
    private readonly CatoDbContext _db;
    private readonly AlertService _alerts;
    private readonly TimeProvider _time;

    public AlertsController(CatoDbContext db, AlertService alerts, TimeProvider time)
    {
        _db = db;
        _alerts = alerts;
        _time = time;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<AlertIncidentDto>), StatusCodes.Status200OK)]
    public async Task<List<AlertIncidentDto>> List([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var rows = await _db.AlertIncidents.AsNoTracking()
            .OrderBy(i => i.ResolvedAt != null)
            .ThenByDescending(i => i.OpenedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        return rows.Select(i => new AlertIncidentDto(
            i.Id, i.JobName, i.Rule, AlertRules.Title(i.Rule), i.Message, i.RunId,
            Utc(i.OpenedAt), Utc(i.LastSeenAt), i.ResolvedAt is { } r ? Utc(r) : null,
            i.OpenSentTo, i.ResolveSentTo)).ToList();
    }

    /// <summary>Sends one test message to every channel; 503 when no channel is configured.</summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(List<AlertChannelResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> Test([FromBody] TestAlertRequest? request, CancellationToken ct)
    {
        var results = await _alerts.SendTestAsync(request?.RequestedBy, _time.GetUtcNow().UtcDateTime, ct);
        return results.Count == 0
            ? Results.Problem("No alert channel is configured (Alerts:SnsTopicArn, Alerts:DiscordWebhookSecretId).",
                statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(results);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
