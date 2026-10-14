using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Games;

/// <summary>
/// The apps the collector's Steamworks login can see (the partner wishlist overview
/// page, or <c>GetPartnerAppListForWebAPIKey</c> with a publisher key). Every one of
/// them is ours, so each is created or promoted to <c>GameType = "Owned"</c>.
/// </summary>
public record SyncOwnedGamesCommand(IReadOnlyList<OwnedGameEntry> Games) : IRequest<Result<SyncOwnedGamesResult>>;

public record OwnedGameEntry(int AppId, string? Name);

public record SyncOwnedGamesResult(int Created, int Promoted, int Unchanged, int Failed, int EnrichmentQueued);
