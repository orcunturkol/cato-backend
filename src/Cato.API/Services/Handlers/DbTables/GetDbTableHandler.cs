using Cato.API.Models.DbTables;
using Cato.API.Services.DbTables;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Cato.API.Services.Handlers.DbTables;

public class GetDbTableHandler : IRequestHandler<GetDbTableQuery, DbTableDetailDto?>
{
    private readonly CatoDbContext _db;
    private readonly DbTableStats _stats;
    private readonly TimeProvider _clock;

    public GetDbTableHandler(CatoDbContext db, DbTableStats stats, TimeProvider clock)
    {
        _db = db;
        _stats = stats;
        _clock = clock;
    }

    public async Task<DbTableDetailDto?> Handle(GetDbTableQuery request, CancellationToken ct)
    {
        // The name reaches SQL only after it matched a mapped table.
        var schema = DbTableSchemaReader.Read(_db.GetService<IDesignTimeModel>().Model)
            .FirstOrDefault(t => t.Table == request.Table);
        if (schema is null) return null;

        var estimates = await _stats.EstimateRowsAsync(ct);
        var exact = await _stats.CountRowsAsync(schema.Table, ct);

        return new DbTableDetailDto(
            _clock.GetUtcNow().UtcDateTime,
            DbTableView.Summary(schema, estimates?.GetValueOrDefault(schema.Table)),
            exact.Rows,
            exact.CountedAt,
            DbTableView.Columns(schema));
    }
}
