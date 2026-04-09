using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestWishlistDataCommand(int AppId, string FileName, Stream Content) : IRequest<IngestionResult>;
