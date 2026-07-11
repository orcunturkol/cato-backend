using System.Text.Json;
using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Ingestion;

public record IngestSpecialEventsCommand(JsonElement Data) : IRequest<IngestionResult>;
