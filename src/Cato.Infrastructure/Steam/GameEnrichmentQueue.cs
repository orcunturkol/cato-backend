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
    /// Picks games in priority order: never-enriched reddit-analysed games (most
    /// recently analysed first), then never-enriched games generally, then — only
    /// when a refresh window is configured — the stalest already-enriched records.
    ///
    /// Games at or past the failure threshold are excluded from the never-enriched
    /// bands entirely: an app with no store page answers the same way every cycle.
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

        // Most recently analysed first — the freshest question gets the freshest data.
        var analysedIds = await neverEnriched
            .Where(g => g.AnalyzedAt != null)
            .OrderByDescending(g => g.AnalyzedAt)
            .Select(g => g.Id)
            .Take(budget)
            .ToListAsync(ct);

        var gameIds = new List<Guid>(analysedIds);
        budget -= analysedIds.Count;

        if (budget > 0 && settings.IncludeUnanalyzedGames)
        {
            var otherIds = await neverEnriched
                .Where(g => g.AnalyzedAt == null)
                .OrderBy(g => g.AppId)
                .Select(g => g.Id)
                .Take(budget)
                .ToListAsync(ct);

            gameIds.AddRange(otherIds);
            budget -= otherIds.Count;
        }

        var refreshCount = 0;
        if (budget > 0 && settings.RefreshAfterDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-settings.RefreshAfterDays);
            var refreshIds = await db.Games
                .Where(g => g.LastEnrichedAt != null && g.LastEnrichedAt < cutoff)
                .OrderBy(g => g.LastEnrichedAt)
                .Select(g => g.Id)
                .Take(budget)
                .ToListAsync(ct);

            gameIds.AddRange(refreshIds);
            refreshCount = refreshIds.Count;
        }

        return new GameEnrichmentBatch(gameIds, analysedIds.Count, refreshCount);
    }
}
