using Carter;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Features.Ingestion;

public static class GetIngestionLogs
{
    public record Query(string? Source, int Limit = 20) : IRequest<List<IngestionLogDto>>;

    public record IngestionLogDto(
        Guid Id,
        string Source,
        DateTime StartTime,
        DateTime? EndTime,
        string Status,
        int RecordsProcessed,
        int RecordsInserted,
        int RecordsUpdated,
        int RecordsFailed,
        string? ErrorMessage,
        string? FilePath);

    public class Handler : IRequestHandler<Query, List<IngestionLogDto>>
    {
        private readonly CatoDbContext _db;

        public Handler(CatoDbContext db) => _db = db;

        public async Task<List<IngestionLogDto>> Handle(Query request, CancellationToken ct)
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

    public class Endpoint : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
            app.MapGet("/api/ingestion/logs", async (string? source, int? limit, IMediator mediator) =>
            {
                var result = await mediator.Send(new Query(source, limit ?? 20));
                return Results.Ok(result);
            })
            .WithTags("Ingestion")
            .WithDescription("Query ingestion logs for monitoring pipeline health");
        }
    }
}
