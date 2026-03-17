using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestSteamDbSnapshotHandler : IRequestHandler<IngestSteamDbSnapshotCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestSteamDbSnapshotHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestSteamDbSnapshotCommand request, CancellationToken ct)
        => _ingestionService.IngestSteamDbSnapshotAsync(request, ct);
}
