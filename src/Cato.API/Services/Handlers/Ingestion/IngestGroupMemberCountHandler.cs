using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class IngestGroupMemberCountHandler : IRequestHandler<IngestGroupMemberCountCommand, IngestionResult>
{
    private readonly IIngestionService _ingestionService;

    public IngestGroupMemberCountHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<IngestionResult> Handle(IngestGroupMemberCountCommand request, CancellationToken ct)
        => _ingestionService.IngestGroupMemberCountAsync(request, ct);
}
