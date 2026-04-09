using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestStoreTrafficCommand(int AppId, string FileName, Stream Content) : IRequest<IngestionResult>;
