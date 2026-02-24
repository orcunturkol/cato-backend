using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestPeakCcuCommand(int AppId, string FilePath) : IRequest<IngestionResult>;
