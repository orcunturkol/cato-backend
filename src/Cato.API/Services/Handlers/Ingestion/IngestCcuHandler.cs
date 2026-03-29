using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestCcuHandler : IRequestHandler<IngestCcuCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestCcuHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestCcuCommand request, CancellationToken ct)
        => _ingestionService.IngestCcuAsync(request, ct);
}
