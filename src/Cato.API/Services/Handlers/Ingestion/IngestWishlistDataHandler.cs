using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestWishlistDataHandler : IRequestHandler<IngestWishlistDataCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestWishlistDataHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestWishlistDataCommand request, CancellationToken ct)
        => _ingestionService.IngestWishlistDataAsync(request, ct);
}
