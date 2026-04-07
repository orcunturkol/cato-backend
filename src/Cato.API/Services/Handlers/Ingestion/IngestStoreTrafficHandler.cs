using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestStoreTrafficHandler : IRequestHandler<IngestStoreTrafficCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestStoreTrafficHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestStoreTrafficCommand request, CancellationToken ct)
        => _ingestionService.IngestStoreTrafficAsync(request, ct);
}
