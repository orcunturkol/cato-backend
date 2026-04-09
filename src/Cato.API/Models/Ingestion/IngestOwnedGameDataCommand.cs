using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestOwnedGameDataCommand(string FileName, Stream Content) : IRequest<IngestionResult>;
