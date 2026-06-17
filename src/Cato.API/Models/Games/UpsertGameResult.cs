using Cato.API.DTOs;

namespace Cato.API.Models.Games;

/// <summary>Outcome of <c>IGameService.UpsertGameAsync</c>: the resulting game and
/// whether it was newly created (<c>true</c>) or an existing game was updated (<c>false</c>).</summary>
public record UpsertGameResult(GameDto Game, bool WasCreated);
