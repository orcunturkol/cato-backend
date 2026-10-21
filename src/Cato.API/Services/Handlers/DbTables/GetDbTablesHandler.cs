using Cato.API.Models.DbTables;
using Cato.API.Services.DbTables;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Cato.API.Services.Handlers.DbTables;

public class GetDbTablesHandler : IRequestHandler<GetDbTablesQuery, DbTablesDto>
{
    private readonly CatoDbContext _db;
    private readonly DbTableStats _stats;
    private readonly TimeProvider _clock;

    public GetDbTablesHandler(CatoDbContext db, DbTableStats stats, TimeProvider clock)
    {
        _db = db;
        _stats = stats;
        _clock = clock;
    }

    public async Task<DbTablesDto> Handle(GetDbTablesQuery request, CancellationToken ct)
    {
        var estimates = await _stats.EstimateRowsAsync(ct);
        var tables = DbTableSchemaReader.Read(_db.GetService<IDesignTimeModel>().Model)
            .Select(t => DbTableView.Summary(t, estimates?.GetValueOrDefault(t.Table)))
            .ToList();

        return new DbTablesDto(_clock.GetUtcNow().UtcDateTime, estimates != null, tables);
    }
}
