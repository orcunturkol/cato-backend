using Cato.API.Models.AppHistory;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.AppHistory;

public class GetChangelistDetailHandler : IRequestHandler<GetChangelistDetailQuery, List<AppChangeRecordDto>>
{
    private readonly CatoDbContext _db;

    public GetChangelistDetailHandler(CatoDbContext db) => _db = db;

    public async Task<List<AppChangeRecordDto>> Handle(GetChangelistDetailQuery request, CancellationToken ct)
    {
        var game = await _db.Games.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == request.GameId, ct);

        if (game is null)
            return [];

        return await _db.AppChangeRecords.AsNoTracking()
            .Where(r => r.AppId == game.AppId && r.ChangeNumber == request.ChangeNumber)
            .OrderBy(r => r.Section)
            .ThenBy(r => r.KeyPath)
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
    }
}
