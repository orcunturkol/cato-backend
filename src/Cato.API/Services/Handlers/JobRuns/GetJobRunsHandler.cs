using Cato.API.Models.JobRuns;
using Cato.API.Services.JobRuns;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.JobRuns;

public class GetJobRunsHandler : IRequestHandler<GetJobRunsQuery, List<JobRunDto>>
{
    private readonly CatoDbContext _db;
    private readonly IJobCatalog _catalog;
    private readonly JobRunStatusResolver _resolver;

    public GetJobRunsHandler(CatoDbContext db, IJobCatalog catalog, JobRunStatusResolver resolver)
    {
        _db = db;
        _catalog = catalog;
        _resolver = resolver;
    }

    public async Task<List<JobRunDto>> Handle(GetJobRunsQuery request, CancellationToken ct)
    {
        var query = _db.JobRuns.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.JobName))
            query = query.Where(j => j.JobName == request.JobName);
        if (!string.IsNullOrWhiteSpace(request.Producer))
            query = query.Where(j => j.Producer == request.Producer);
        // Filters on the stored status; a "Lost" row is stored as "Running".
        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(j => j.Status == request.Status);

        var limit = Math.Clamp(request.Limit, 1, 500);

        var rows = await query
            .OrderByDescending(j => j.StartTime)
            .Take(limit)
            .ToListAsync(ct);

        var catalog = await _catalog.LoadAsync(ct);
        return await _resolver.ToDtosAsync(rows, catalog, ct);
    }
}
