using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record ReEnrichAllGamesCommand : IRequest<Result<ReEnrichAllGamesResult>>;

public record ReEnrichAllGamesResult(int Total, int Enriched, int Failed, List<string> Errors);
