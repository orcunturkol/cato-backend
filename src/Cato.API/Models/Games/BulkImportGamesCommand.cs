using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record BulkImportGamesCommand(string FileName, Stream Content, string? GameType = null) : IRequest<Result<BulkImportResult>>;

public record BulkImportResult(int Queued);
