using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestGroupMemberCountCommand(int AppId, string FilePath) : IRequest<IngestionResult>;
