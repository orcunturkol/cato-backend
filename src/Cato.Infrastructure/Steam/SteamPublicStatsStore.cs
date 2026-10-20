using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Cato.Infrastructure.Steam.Models;
using Microsoft.EntityFrameworkCore;

namespace Cato.Infrastructure.Steam;

/// <summary>
/// Writes of the keyless public Steam stats the watchers fetch: achievement unlock
/// percentages (a daily snapshot per achievement) and game news (upsert by gid, the
/// same mapping as the manual news upload). The caller saves.
/// </summary>
public static class SteamPublicStatsStore
{
    public static async Task<(int Added, int Updated)> UpsertAchievementPercentsAsync(
        CatoDbContext db, Guid gameId, int appId, DateOnly date, IReadOnlyList<SteamAchievementPercent> percents, CancellationToken ct)
    {
        var rows = percents
            .Where(p => !string.IsNullOrEmpty(p.Name) && p.Name.Length <= 200 && p.Percent is >= 0 and <= 100)
            .GroupBy(p => p.Name)
            .ToDictionary(g => g.Key, g => decimal.Round(g.Last().Percent, 3));
        var existing = await db.GameAchievementPercents
            .Where(p => p.GameId == gameId && p.SnapshotDate == date)
            .ToDictionaryAsync(p => p.ApiName, ct);

        int added = 0, updated = 0;
        foreach (var (name, percent) in rows)
        {
            if (existing.TryGetValue(name, out var current))
            {
                if (current.Percent == percent) continue;
                current.Percent = percent;
                updated++;
                continue;
            }
            db.GameAchievementPercents.Add(new GameAchievementPercent
            {
                Id = Guid.NewGuid(), GameId = gameId, AppId = appId, ApiName = name, SnapshotDate = date, Percent = percent,
            });
            added++;
        }
        return (added, updated);
    }

    public static async Task<(int Added, int Updated)> UpsertNewsAsync(
        CatoDbContext db, Guid gameId, IReadOnlyList<SteamNewsItem> items, CancellationToken ct)
    {
        var byGid = items
            .Where(i => !string.IsNullOrEmpty(i.Gid) && i.Gid.Length <= 500)
            .GroupBy(i => i.Gid)
            .ToDictionary(g => g.Key, g => g.Last());
        var gids = byGid.Keys.ToList();
        var existing = await db.GameNews
            .Where(n => n.GameId == gameId && gids.Contains(n.ExternalId))
            .ToDictionaryAsync(n => n.ExternalId, ct);

        int added = 0, updated = 0;
        foreach (var (gid, item) in byGid)
        {
            var title = Truncate(item.Title, 500) ?? string.Empty;
            var url = Truncate(item.Url, 1000);
            var author = Truncate(item.Author, 200);
            var feedLabel = Truncate(item.FeedLabel, 200);
            var publishedAt = DateTimeOffset.FromUnixTimeSeconds(item.Date).UtcDateTime;
            if (existing.TryGetValue(gid, out var current))
            {
                if (current.Title == title && current.Url == url && current.Author == author && current.Contents == item.Contents
                    && current.FeedLabel == feedLabel && current.PublishedAt == publishedAt)
                    continue;
                (current.Title, current.Url, current.Author, current.Contents, current.FeedLabel, current.PublishedAt)
                    = (title, url, author, item.Contents, feedLabel, publishedAt);
                updated++;
                continue;
            }
            db.GameNews.Add(new GameNews
            {
                Id = Guid.NewGuid(), GameId = gameId, ExternalId = gid, Source = "news", Title = title, Url = url,
                Author = author, Contents = item.Contents, PublishedAt = publishedAt, FeedLabel = feedLabel,
            });
            added++;
        }
        return (added, updated);
    }

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
