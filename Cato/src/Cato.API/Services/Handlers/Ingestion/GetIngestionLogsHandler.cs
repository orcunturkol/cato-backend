using Cato.API.Models.Ingestion;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetIngestionLogsHandler : IRequestHandler<GetIngestionLogsQuery, List<IngestionLogDto>>
{
    private readonly CatoDbContext _db;

    public GetIngestionLogsHandler(CatoDbContext db) => _db = db;

    public async Task<List<IngestionLogDto>> Handle(GetIngestionLogsQuery request, CancellationToken ct)
    {
        var query = _db.IngestionLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Source))
            query = query.Where(l => l.Source == request.Source);

        var logs = await query
            .OrderByDescending(l => l.StartTime)
            .Take(request.Limit)
            .Select(l => new IngestionLogDto(
                l.Id,
                l.Source,
                l.StartTime,
                l.EndTime,
                l.Status,
                l.RecordsProcessed,
                l.RecordsInserted,
                l.RecordsUpdated,
                l.RecordsFailed,
                l.ErrorMessage,
                l.FilePath))
            .ToListAsync(ct);

        return logs;
    }
}
