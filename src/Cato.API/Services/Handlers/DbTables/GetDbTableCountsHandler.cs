using Cato.API.Models.DbTables;
using Cato.API.Services.DbTables;
using MediatR;

namespace Cato.API.Services.Handlers.DbTables;

public class GetDbTableCountsHandler : IRequestHandler<GetDbTableCountsQuery, DbTableCountsDto>
{
    private readonly DbTableStats _stats;
    private readonly TimeProvider _clock;

    public GetDbTableCountsHandler(DbTableStats stats, TimeProvider clock)
    {
        _stats = stats;
        _clock = clock;
    }

    public async Task<DbTableCountsDto> Handle(GetDbTableCountsQuery request, CancellationToken ct)
    {
        var estimates = await _stats.EstimateRowsAsync(ct);
        return new DbTableCountsDto(_clock.GetUtcNow().UtcDateTime, estimates != null, estimates ?? []);
    }
}
