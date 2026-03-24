using Cato.API.DTOs;
using Cato.API.Models.AppHistory;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.AppHistory;

public class GetChangelistsHandler : IRequestHandler<GetChangelistsQuery, PagedResult<ChangelistSummaryDto>>
{
    private readonly CatoDbContext _db;

    public GetChangelistsHandler(CatoDbContext db) => _db = db;

    public async Task<PagedResult<ChangelistSummaryDto>> Handle(GetChangelistsQuery request, CancellationToken ct)
    {
        var game = await _db.Games.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == request.GameId, ct);

        if (game is null)
            return new PagedResult<ChangelistSummaryDto> { Page = request.Page, PageSize = request.PageSize };

        var query = _db.AppChangeRecords.AsNoTracking()
            .Where(r => r.AppId == game.AppId);

        // Group by changelist
        var groupedQuery = query
            .GroupBy(r => new { r.ChangeNumber, r.DetectedAt })
            .Select(g => new ChangelistSummaryDto
            {
                ChangeNumber = g.Key.ChangeNumber,
                DetectedAt = g.Key.DetectedAt,
                ChangeCount = g.Count(),
                SectionsAffected = g.Select(r => r.Section).Distinct().ToList()
            })
            .OrderByDescending(c => c.ChangeNumber);

        var totalCount = await groupedQuery.CountAsync(ct);

        var items = await groupedQuery
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ChangelistSummaryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}
