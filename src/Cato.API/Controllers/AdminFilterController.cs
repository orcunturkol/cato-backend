using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam.Filtering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Controllers;

[ApiController]
[Route("admin/filter")]
[Tags("Admin / Filter")]
public class AdminFilterController : ControllerBase
{
    private readonly CatoDbContext _db;
    private readonly IGameQualityFilter _filter;
    private readonly ILogger<AdminFilterController> _logger;

    public AdminFilterController(CatoDbContext db, IGameQualityFilter filter, ILogger<AdminFilterController> logger)
    {
        _db = db;
        _filter = filter;
        _logger = logger;
    }

    public record BackfillResult(int Scanned, int Rejected, int Accepted, Dictionary<string, int> ByReason, bool DryRun);

    [HttpPost("backfill")]
    [ProducesResponseType(typeof(BackfillResult), StatusCodes.Status200OK)]
    public async Task<IResult> Backfill([FromQuery] bool dryRun, CancellationToken ct)
    {
        var games = await _db.Games
            .AsNoTracking()
            .AsSplitQuery()
            .Include(g => g.Tags)
            .Include(g => g.Developer)
            .Include(g => g.Publisher)
            .ToListAsync(ct);

        var byReason = new Dictionary<string, int>();
        var idsByReason = new Dictionary<string, List<Guid>>();

        foreach (var game in games)
        {
            var decision = _filter.Evaluate(game, FilterStage.Backfill);
            if (!decision.Rejected) continue;

            var reason = decision.Reason ?? "unknown";
            byReason[reason] = byReason.GetValueOrDefault(reason) + 1;

            if (!dryRun)
            {
                if (!idsByReason.TryGetValue(reason, out var list))
                    idsByReason[reason] = list = new List<Guid>();
                list.Add(game.Id);
            }
        }

        var rejected = byReason.Values.Sum();

        if (!dryRun && idsByReason.Count > 0)
        {
            var now = DateTime.UtcNow;
            const int chunkSize = 1000;
            foreach (var (reason, ids) in idsByReason)
            {
                for (var i = 0; i < ids.Count; i += chunkSize)
                {
                    var chunk = ids.GetRange(i, Math.Min(chunkSize, ids.Count - i));
                    await _db.Games
                        .Where(g => chunk.Contains(g.Id))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(g => g.IsFiltered, true)
                            .SetProperty(g => g.FilterReason, reason)
                            .SetProperty(g => g.FilteredAt, now), ct);
                }
            }
        }

        _logger.LogInformation("Filter backfill complete (dryRun={DryRun}): scanned={Scanned} rejected={Rejected}", dryRun, games.Count, rejected);
        return Results.Ok(new BackfillResult(games.Count, rejected, games.Count - rejected, byReason, dryRun));
    }

    [HttpGet("report")]
    [ProducesResponseType(typeof(Dictionary<string, int>), StatusCodes.Status200OK)]
    public async Task<IResult> Report(CancellationToken ct)
    {
        var byReason = await _db.Games
            .Where(g => g.IsFiltered && g.FilterReason != null)
            .GroupBy(g => g.FilterReason!)
            .Select(grp => new { Reason = grp.Key, Count = grp.Count() })
            .ToListAsync(ct);

        return Results.Ok(byReason.ToDictionary(r => r.Reason, r => r.Count));
    }

    public record DeleteResult(int Deleted);

    [HttpDelete("flagged")]
    [ProducesResponseType(typeof(DeleteResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IResult> DeleteFlagged([FromQuery] bool confirm, CancellationToken ct)
    {
        if (!confirm)
            return Results.BadRequest("Pass confirm=true to delete. This cascade-deletes related rows in ~15 tables.");

        var deleted = await _db.Games
            .Where(g => g.IsFiltered)
            .ExecuteDeleteAsync(ct);

        _logger.LogWarning("Filter: deleted {Count} flagged games (cascade applies)", deleted);
        return Results.Ok(new DeleteResult(deleted));
    }
}
