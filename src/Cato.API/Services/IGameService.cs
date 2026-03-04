using Cato.API.DTOs;
using Cato.API.Models.Games;

namespace Cato.API.Services;

public interface IGameService
{
    Task<Result<GameDto>> CreateGameAsync(CreateGameCommand command, CancellationToken ct = default);
    Task<PagedResult<GameDto>> ListGamesAsync(ListGamesQuery query, CancellationToken ct = default);
    Task<Result<GameDto>> GetGameDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Result<GameDto>> UpdateGameAsync(UpdateGameCommand command, CancellationToken ct = default);
    Task<Result<bool>> DeleteGameAsync(Guid id, CancellationToken ct = default);
    Task<Result<GameDto>> EnrichGameFromSteamAsync(Guid id, CancellationToken ct = default);
}
