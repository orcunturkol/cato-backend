using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestPatchNotesHandler : IRequestHandler<IngestPatchNotesCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestPatchNotesHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestPatchNotesCommand request, CancellationToken ct)
        => _ingestionService.IngestPatchNotesAsync(request, ct);
}
