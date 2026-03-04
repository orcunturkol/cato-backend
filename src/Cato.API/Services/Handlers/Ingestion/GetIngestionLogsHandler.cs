using Cato.API.DTOs;
using Cato.API.Models.Ingestion;
using Cato.API.Services;
using MediatR;

namespace Cato.API.Services.Handlers.Ingestion;

public class GetIngestionLogsHandler : IRequestHandler<GetIngestionLogsQuery, List<IngestionLogDto>>
{
    private readonly IIngestionService _ingestionService;

    public GetIngestionLogsHandler(IIngestionService ingestionService) => _ingestionService = ingestionService;

    public Task<List<IngestionLogDto>> Handle(GetIngestionLogsQuery request, CancellationToken ct)
        => _ingestionService.GetIngestionLogsAsync(request, ct);
}
