using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestSpecialEventsHandler : IRequestHandler<IngestSpecialEventsCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestSpecialEventsHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestSpecialEventsCommand request, CancellationToken ct)
        => _ingestionService.IngestSpecialEventsAsync(request, ct);
}
