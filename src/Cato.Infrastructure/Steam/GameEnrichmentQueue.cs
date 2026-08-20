using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.Infrastructure.Steam;

/// <summary>One cycle's worth of work, and where each game in it came from.</summary>
public record GameEnrichmentBatch(IReadOnlyList<Guid> GameIds, int Analysed, int Refresh)
{
    public static readonly GameEnrichmentBatch Empty = new([], 0, 0);
}

/// <summary>
/// Chooses which games <see cref="GameEnrichmentWatcherService"/> enriches next.
/// Separate from the watcher because the ordering is the part worth testing — the
/// watcher itself is a timer around it.
/// </summary>
public static class GameEnrichmentQueue
{
    /// <summary>
    /// Picks games in priority order, in four bands:
    ///
    /// <list type="number">
    /// <item>reddit-analysed games with no store record at all</item>
    /// <item>reddit-analysed games whose record has gone stale</item>
    /// <item>everything else with no store record, by app id</item>
    /// <item>everything else that has gone stale, stalest first</item>
    /// </list>
    ///
    /// Both reddit bands come before either general one, deliberately. Ordering
    /// purely by "never enriched first" would put ~9k discovered games ahead of a
    /// stale reddit game, so a refresh of the games someone is actually asking about
    /// would not start for two days.
    ///
    /// Bands 2 and 4 exist only when <see cref="GameEnrichmentSettings.RefreshAfterDays"/>
    /// is set; bands 3 and 4 only when
    /// <see cref="GameEnrichmentSettings.IncludeUnanalyzedGames"/> is on. Games at or
    /// past the failure threshold are excluded from the never-enriched bands: an app
    /// with no store page answers the same way every cycle.
    /// </summary>
    public static async Task<GameEnrichmentBatch> SelectAsync(
        CatoDbContext db,
        GameEnrichmentSettings settings,
        CancellationToken ct = default)
    {
        var budget = settings.BatchSize;
        if (budget <= 0) return GameEnrichmentBatch.Empty;

        var neverEnriched = db.Games
            .Where(g => g.LastEnrichedAt == null)
            .Where(g => g.EnrichmentFailures < settings.FailureThreshold);

        var stale = settings.RefreshAfterDays > 0
            ? db.Games.Where(g => g.LastEnrichedAt != null
                                  && g.LastEnrichedAt < DateTime.UtcNow.AddDays(-settings.RefreshAfterDays))
            : null;

        var gameIds = new List<Guid>();
        var analysed = 0;
        var refreshed = 0;

        async Task<int> TakeAsync(IQueryable<Guid> query)
        {
            if (budget <= 0) return 0;
            var ids = await query.Take(budget).ToListAsync(ct);
            gameIds.AddRange(ids);
            budget -= ids.Count;
            return ids.Count;
        }

        // 1. Never-enriched reddit games — most recently analysed first, so the
        //    freshest question gets the freshest data.
        analysed += await TakeAsync(neverEnriched
            .Where(g => g.AnalyzedAt != null)
            .OrderByDescending(g => g.AnalyzedAt)
            .Select(g => g.Id));

        // 2. Stale reddit games.
        if (stale is not null)
        {
            var n = await TakeAsync(stale
                .Where(g => g.AnalyzedAt != null)
                .OrderByDescending(g => g.AnalyzedAt)
                .Select(g => g.Id));
            analysed += n;
            refreshed += n;
        }

        if (settings.IncludeUnanalyzedGames)
        {
            // 3. Everything else that has never been enriched.
            await TakeAsync(neverEnriched
                .Where(g => g.AnalyzedAt == null)
                .OrderBy(g => g.AppId)
                .Select(g => g.Id));

            // 4. Everything else that has gone stale.
            if (stale is not null)
                refreshed += await TakeAsync(stale
                    .Where(g => g.AnalyzedAt == null)
                    .OrderBy(g => g.LastEnrichedAt)
                    .Select(g => g.Id));
        }

        return new GameEnrichmentBatch(gameIds, analysed, refreshed);
    }
}
