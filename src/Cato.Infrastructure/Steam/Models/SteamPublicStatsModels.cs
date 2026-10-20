namespace Cato.Infrastructure.Steam.Models;

/// <summary>One achievement's global unlock percentage (GetGlobalAchievementPercentagesForApp).</summary>
public record SteamAchievementPercent(string Name, decimal Percent);

/// <summary>One item of ISteamNews/GetNewsForApp; <c>Date</c> is unix seconds.</summary>
public record SteamNewsItem(string Gid, string Title, string? Url, string? Author, string? Contents, string? FeedLabel, long Date);
