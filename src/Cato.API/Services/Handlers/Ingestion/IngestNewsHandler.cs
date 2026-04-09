using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestNewsHandler : IRequestHandler<IngestNewsCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestNewsHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestNewsCommand request, CancellationToken ct)
        => _ingestionService.IngestNewsAsync(request, ct);
}
