using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

public record ReEnrichAllGamesCommand : IRequest<Result<ReEnrichAllGamesResult>>;

public record ReEnrichAllGamesResult(int Total, int Enriched, int Failed, List<string> Errors);

/// <summary>
/// Fire-and-forget version: queues enrichment in the background and returns immediately
/// with the count of games that will be enriched.
/// </summary>
public record ReEnrichAllGamesBackgroundCommand : IRequest<Result<ReEnrichAllGamesBackgroundResult>>;

public record ReEnrichAllGamesBackgroundResult(int QueuedCount);
