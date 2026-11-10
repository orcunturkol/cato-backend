using Cato.API.DTOs;
using Cato.API.Models.Games;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam;
using MediatR;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.Handlers.Games;

public class RefreshMainCapsulesHandler(
    CatoDbContext db,
    ISteamApiService steam,
    IOutputCacheStore outputCache,
    ILogger<RefreshMainCapsulesHandler> logger)
    : IRequestHandler<RefreshMainCapsulesCommand, Result<RefreshMainCapsulesResult>>
{
    public async Task<Result<RefreshMainCapsulesResult>> Handle(RefreshMainCapsulesCommand request, CancellationToken ct)
    {
        var appIds = request.AppIds.Distinct().ToList();
        if (appIds.Count > RefreshMainCapsulesCommand.MaxAppIds)
            return Result<RefreshMainCapsulesResult>.Failure(
                $"At most {RefreshMainCapsulesCommand.MaxAppIds} app ids per request.");

        var games = await db.Games.Where(g => appIds.Contains(g.AppId)).ToListAsync(ct);
        var updated = 0;
        var failed = 0;

        foreach (var batch in games.Chunk(SteamApiService.MaxCapsuleBatch))
        {
            IReadOnlyDictionary<int, string> urls;
            try
            {
                urls = await steam.GetMainCapsuleUrlsAsync(batch.Select(g => g.AppId).ToList(), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Main capsule batch of {Count} games failed", batch.Length);
                failed++;
                continue;
            }

            foreach (var game in batch)
            {
                if (!urls.TryGetValue(game.AppId, out var url) || url == game.CapsuleImageUrl) continue;
                game.CapsuleImageUrl = url;
                updated++;
            }
            await db.SaveChangesAsync(ct);
        }

        // Same tag as an enrichment: sibling services see the new URLs now, not after the TTL.
        if (updated > 0)
            await outputCache.EvictByTagAsync("games-catalog", ct);

        logger.LogInformation("Main capsules: {Requested} requested, {Known} known, {Updated} updated, {Failed} failed batches",
            appIds.Count, games.Count, updated, failed);
        return Result<RefreshMainCapsulesResult>.Success(new(appIds.Count, games.Count, updated, failed));
    }
}
