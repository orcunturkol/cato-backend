using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestActiveUsersHistoryHandler : IRequestHandler<IngestActiveUsersHistoryCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestActiveUsersHistoryHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestActiveUsersHistoryCommand request, CancellationToken ct)
        => _ingestionService.IngestActiveUsersHistoryAsync(request, ct);
}
