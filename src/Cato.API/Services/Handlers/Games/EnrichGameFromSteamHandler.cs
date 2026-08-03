using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.API.Services;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;

namespace Cato.API.Services.Handlers.Games;

public class EnrichGameFromSteamHandler : IRequestHandler<EnrichGameFromSteamCommand, Result<GameDto>>
{
    private readonly IGameService _gameService;
    private readonly IOutputCacheStore _outputCache;

    public EnrichGameFromSteamHandler(IGameService gameService, IOutputCacheStore outputCache)
    {
        _gameService = gameService;
        _outputCache = outputCache;
    }

    public async Task<Result<GameDto>> Handle(EnrichGameFromSteamCommand request, CancellationToken ct)
    {
        var result = await _gameService.EnrichGameFromSteamAsync(request.Id, ct);

        // Enrichment changed what the catalog serves — drop the cached pages so
        // sibling services see fresh media immediately instead of after TTL.
        if (result.IsSuccess)
            await _outputCache.EvictByTagAsync("games-catalog", ct);

        return result;
    }
}
