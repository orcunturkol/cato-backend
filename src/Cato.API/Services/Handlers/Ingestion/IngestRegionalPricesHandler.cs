using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestRegionalPricesHandler : IRequestHandler<IngestRegionalPricesCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestRegionalPricesHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestRegionalPricesCommand request, CancellationToken ct)
        => _ingestionService.IngestRegionalPricesAsync(request, ct);
}
