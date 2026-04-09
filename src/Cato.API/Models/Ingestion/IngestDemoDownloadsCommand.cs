using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestDemoDownloadsCommand(int AppId, int? DemoAppId, string FileName, Stream Content) : IRequest<IngestionResult>;
