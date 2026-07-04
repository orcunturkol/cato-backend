using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestSpecialEventsCommand(string FilePath) : IRequest<IngestionResult>;
