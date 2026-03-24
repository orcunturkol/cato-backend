using Cato.API.DTOs;
using Cato.API.Models.AppHistory;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.AppHistory;

public class GetChangeHistoryHandler : IRequestHandler<GetChangeHistoryQuery, PagedResult<AppChangeRecordDto>>
{
    private readonly CatoDbContext _db;

    public GetChangeHistoryHandler(CatoDbContext db) => _db = db;

    public async Task<PagedResult<AppChangeRecordDto>> Handle(GetChangeHistoryQuery request, CancellationToken ct)
    {
        var game = await _db.Games.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == request.GameId, ct);

        if (game is null)
            return new PagedResult<AppChangeRecordDto> { Page = request.Page, PageSize = request.PageSize };

        var query = _db.AppChangeRecords.AsNoTracking()
            .Where(r => r.AppId == game.AppId);

        if (!string.IsNullOrEmpty(request.Section))
            query = query.Where(r => r.Section == request.Section);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.DetectedAt)
            .ThenBy(r => r.KeyPath)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new AppChangeRecordDto
            {
                ChangeNumber = r.ChangeNumber,
                Section = r.Section,
                KeyPath = r.KeyPath,
                Action = r.Action,
                OldValue = r.OldValue,
                NewValue = r.NewValue,
                DetectedAt = r.DetectedAt
            })
            .ToListAsync(ct);

        return new PagedResult<AppChangeRecordDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }
}
