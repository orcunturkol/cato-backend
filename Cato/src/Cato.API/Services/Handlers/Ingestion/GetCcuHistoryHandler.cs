using Cato.API.Models.Ingestion;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetCcuHistoryHandler : IRequestHandler<GetCcuHistoryQuery, List<CcuDto>>
{
    private readonly CatoDbContext _db;
    public GetCcuHistoryHandler(CatoDbContext db) => _db = db;

    public async Task<List<CcuDto>> Handle(GetCcuHistoryQuery request, CancellationToken ct)
    {
        var query = _db.CcuHistories
            .AsNoTracking()
            .Include(c => c.Game)
            .Where(c => c.Game.AppId == request.AppId);

        if (!string.IsNullOrWhiteSpace(request.Source))
            query = query.Where(c => c.Source == request.Source);

        return await query
            .OrderByDescending(c => c.Timestamp)
            .Take(request.Limit)
            .Select(c => new CcuDto(c.Id, c.Timestamp, c.CcuCount, c.Source))
            .ToListAsync(ct);
    }
}
