using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam.Models;
using Microsoft.EntityFrameworkCore;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// A game's review summary (the query_summary of its first review page) as a dated snapshot,
/// and the newest snapshot copied to Game.SteamReviewScore and Game.ReviewCount. Shared by the
/// full review sync (<see cref="SteamReviewWatcherService"/>) and the review totals of listed
/// games (<see cref="ReviewTotalWatcherService"/>).
/// </summary>
public static class ReviewSummaries
{
    public static async Task AddSnapshotAsync(
        CatoDbContext db, Guid gameId, SteamReviewQuerySummary summary, DateOnly date, CancellationToken ct)
    {
        db.ReviewSummarySnapshots.Add(new ReviewSummarySnapshot
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            SnapshotDate = date,
            ReviewScore = summary.ReviewScore,
            ReviewScoreDesc = summary.ReviewScoreDesc ?? string.Empty,
            TotalPositive = summary.TotalPositive,
            TotalNegative = summary.TotalNegative,
            TotalReviews = summary.TotalReviews,
        });

        await db.SaveChangesAsync(ct);
    }

    public static async Task RefreshGameFieldsAsync(CatoDbContext db, Guid gameId, CancellationToken ct)
    {
        var latest = await db.ReviewSummarySnapshots
            .Where(s => s.GameId == gameId)
            .OrderByDescending(s => s.SnapshotDate)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latest is null) return;

        var game = await db.Games.FindAsync([gameId], ct);
        if (game is null) return;

        game.SteamReviewScore = latest.ReviewScoreDesc;
        game.ReviewCount = latest.TotalReviews;
        await db.SaveChangesAsync(ct);
    }
}
