using System.Text.Json;
using Cato.API.Models.JobRuns;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using MediatR;

namespace Cato.API.Services.Handlers.JobRuns;

/// <summary>Replaces the producer's schedule with the one it just published.</summary>
public class PublishJobScheduleHandler : IRequestHandler<PublishJobScheduleCommand, JobScheduleSourceDto>
{
    private readonly CatoDbContext _db;
    private readonly TimeProvider _time;

    public PublishJobScheduleHandler(CatoDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<JobScheduleSourceDto> Handle(PublishJobScheduleCommand request, CancellationToken ct)
    {
        var snapshot = await _db.JobScheduleSnapshots.FindAsync([request.Producer], ct);
        if (snapshot is null)
        {
            snapshot = new JobScheduleSnapshot { Producer = request.Producer };
            _db.JobScheduleSnapshots.Add(snapshot);
        }

        snapshot.Source = request.Source;
        snapshot.SourceHost = request.SourceHost;
        snapshot.EntriesJson = JsonSerializer.Serialize(request.Entries, JobCatalog.EntryJson);
        snapshot.PublishedAt = _time.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(ct);

        return new JobScheduleSourceDto(
            snapshot.Producer, snapshot.Source, snapshot.SourceHost,
            DateTime.SpecifyKind(snapshot.PublishedAt, DateTimeKind.Utc), request.Entries.Count);
    }
}
