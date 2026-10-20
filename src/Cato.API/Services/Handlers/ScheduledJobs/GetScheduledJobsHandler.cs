using Cato.API.Models.ScheduledJobs;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.ScheduledJobs;

public class GetScheduledJobsHandler : IRequestHandler<GetScheduledJobsQuery, List<ScheduledJobDto>>
{
    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public GetScheduledJobsHandler(CatoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<List<ScheduledJobDto>> Handle(GetScheduledJobsQuery request, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var jobs = await _db.ScheduledJobs.AsNoTracking()
            .OrderBy(j => j.AgentGroup).ThenBy(j => j.Name)
            .ToListAsync(ct);
        return jobs.Select(j => ScheduledJobMapping.ToDto(j, now)).ToList();
    }
}
