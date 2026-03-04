using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestFinancialDataHandler : IRequestHandler<IngestFinancialDataCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestFinancialDataHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestFinancialDataCommand request, CancellationToken ct)
        => _ingestionService.IngestFinancialDataAsync(request, ct);
}
