using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestOwnedGameDataCommand(int AppId, string FilePath) : IRequest<IngestionResult>;
