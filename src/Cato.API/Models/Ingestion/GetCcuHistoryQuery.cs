using MediatR;

namespace Cato.API.Models.Ingestion;

public record GetCcuHistoryQuery(int AppId, string? Source, int Limit = 100) : IRequest<List<CcuDto>>;

public record CcuDto(
    Guid Id,
    DateTime Timestamp,
    int CcuCount,
    string? Source);
