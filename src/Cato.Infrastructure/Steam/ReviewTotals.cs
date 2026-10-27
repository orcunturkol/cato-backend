using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam.Models;
using Cato.Infrastructure.Steam.SteamKit;
using Microsoft.EntityFrameworkCore;

namespace Cato.Infrastructure.Steam;

/// <summary>A listed game whose review total is due; LastSnapshot is null when it was never read.</summary>
public sealed record ReviewTotalDue(Guid GameId, int AppId, DateOnly? LastSnapshot);

/// <param name="Games">This cycle's games, in reading order.</param>
/// <param name="Due">Every listed game that is due, also those left for later cycles.</param>
public sealed record ReviewTotalBatch(IReadOnlyList<ReviewTotalDue> Games, int Due);

public sealed record ReviewTotalCycle(int Read, IReadOnlyList<int> FailedAppIds);

/// <summary>
/// Which listed games <see cref="ReviewTotalWatcherService"/> reads next, and the reading itself.
/// Separate from the watcher because these are the parts worth testing; the watcher is a timer
/// around them.
/// </summary>
public static class ReviewTotals
{
    /// <summary>
    /// Listed games CATO knows and does not sync in full (not Sourcing, Owned or OGT: the daily
    /// review sync owns those), whose newest review snapshot is RefreshDays old or older. Games
    /// never read come first, then the stalest. An app id CATO does not know is skipped, never
    /// created. <paramref name="skip"/> holds the games that failed lately.
    /// </summary>
    public static async Task<ReviewTotalBatch> SelectAsync(
        CatoDbContext db,
        IReadOnlyCollection<int> appIds,
        SteamSettings settings,
        DateOnly today,
        IReadOnlySet<int> skip,
        CancellationToken ct = default)
    {
        if (appIds.Count == 0)
            return new ReviewTotalBatch([], 0);

        var ids = appIds.ToList();
        var cutoff = today.AddDays(-settings.ReviewTotalsRefreshDays);
        var due = (await db.Games.AsNoTracking()
                .Where(g => ids.Contains(g.AppId) && !GameTypes.Tracked.Contains(g.GameType))
                .Select(g => new
                {
                    g.Id,
                    g.AppId,
                    Last = db.ReviewSummarySnapshots.Where(s => s.GameId == g.Id).Max(s => (DateOnly?)s.SnapshotDate),
                })
                .Where(g => g.Last == null || g.Last <= cutoff)
                .OrderBy(g => g.Last != null)
                .ThenBy(g => g.Last)
                .ThenBy(g => g.AppId)
                .ToListAsync(ct))
            .Where(g => !skip.Contains(g.AppId))
            .Select(g => new ReviewTotalDue(g.Id, g.AppId, g.Last))
            .ToList();

        return new ReviewTotalBatch(due.Take(Math.Max(0, settings.ReviewTotalsBatchSize)).ToList(), due.Count);
    }

    /// <summary>
    /// Reads each game's first review page and stores its summary as today's snapshot. A game with
    /// no reviews is stored as 0. An answer without a score text (Steam's empty answer for an app
    /// it does not know), no answer or an error counts as a failure, and the next game follows.
    /// </summary>
    public static async Task<ReviewTotalCycle> ReadAsync(
        CatoDbContext db,
        IReadOnlyList<ReviewTotalDue> games,
        Func<int, CancellationToken, Task<SteamReviewQuerySummary?>> readSummary,
        DateOnly today,
        CancellationToken ct = default)
    {
        var read = 0;
        var failed = new List<int>();
        foreach (var game in games)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var summary = await readSummary(game.AppId, ct);
                if (summary?.ReviewScoreDesc is null)
                {
                    failed.Add(game.AppId);
                    continue;
                }

                await ReviewSummaries.AddSnapshotAsync(db, game.GameId, summary, today, ct);
                await ReviewSummaries.RefreshGameFieldsAsync(db, game.GameId, ct);
                read++;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear(); // a failed save must not ride along with the next game's
                failed.Add(game.AppId);
            }
        }

        return new ReviewTotalCycle(read, failed);
    }
}
