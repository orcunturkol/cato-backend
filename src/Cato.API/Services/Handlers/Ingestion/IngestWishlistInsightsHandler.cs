using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestWishlistInsightsHandler : IRequestHandler<IngestWishlistInsightsCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestWishlistInsightsHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestWishlistInsightsCommand request, CancellationToken ct)
        => _ingestionService.IngestWishlistInsightsAsync(request, ct);
}
