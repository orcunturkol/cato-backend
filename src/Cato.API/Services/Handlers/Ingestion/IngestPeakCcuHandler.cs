using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestPeakCcuHandler : IRequestHandler<IngestPeakCcuCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestPeakCcuHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestPeakCcuCommand request, CancellationToken ct)
        => _ingestionService.IngestPeakCcuAsync(request, ct);
}
