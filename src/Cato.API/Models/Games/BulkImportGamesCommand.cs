using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record BulkImportGamesCommand(string FileName, Stream Content) : IRequest<Result<BulkImportResult>>;

public record BulkImportResult(int TotalParsed, int Created, int Enriched, int Skipped, List<string> Errors);
