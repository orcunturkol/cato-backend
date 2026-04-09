using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestDemoDownloadsHandler : IRequestHandler<IngestDemoDownloadsCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestDemoDownloadsHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestDemoDownloadsCommand request, CancellationToken ct)
        => _ingestionService.IngestDemoDownloadsAsync(request, ct);
}
