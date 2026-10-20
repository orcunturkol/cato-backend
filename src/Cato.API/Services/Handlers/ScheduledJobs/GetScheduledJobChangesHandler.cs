using System.Text.Json;
using Cato.API.Models.ScheduledJobs;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.ScheduledJobs;

public class GetScheduledJobChangesHandler : IRequestHandler<GetScheduledJobChangesQuery, List<ScheduledJobChangeDto>>
{
    private readonly CatoDbContext _db;

    public GetScheduledJobChangesHandler(CatoDbContext db) => _db = db;

    public async Task<List<ScheduledJobChangeDto>> Handle(GetScheduledJobChangesQuery query, CancellationToken ct)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var changes = await _db.ScheduledJobChanges.AsNoTracking()
            .Where(c => c.JobName == query.Name)
            .OrderByDescending(c => c.ChangedAt)
            .Take(limit)
            .ToListAsync(ct);

        return changes.Select(c => new ScheduledJobChangeDto(
            c.Id,
            c.JobName,
            DateTime.SpecifyKind(c.ChangedAt, DateTimeKind.Utc),
            c.ChangedBy,
            c.Reason,
            JsonDocument.Parse(c.OldJson).RootElement.Clone(),
            JsonDocument.Parse(c.NewJson).RootElement.Clone())).ToList();
    }
}
