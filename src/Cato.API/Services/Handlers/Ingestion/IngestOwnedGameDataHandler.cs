using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestOwnedGameDataHandler : IRequestHandler<IngestOwnedGameDataCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestOwnedGameDataHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestOwnedGameDataCommand request, CancellationToken ct)
        => _ingestionService.IngestOwnedGameDataAsync(request, ct);
}
