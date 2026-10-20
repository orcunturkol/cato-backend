using MediatR;

namespace Cato.API.Models.Ingestion;

/// <summary>
/// A game's achievements from game_achievement_schema with the latest global unlock
/// percentage (Owned games; null when no snapshot), most unlocked first.
/// </summary>
public record GetAchievementsQuery(int AppId) : IRequest<AchievementsDto>;

public record AchievementsDto(DateOnly? SnapshotDate, List<AchievementDto> Achievements);

public record AchievementDto(string ApiName, string? DisplayName, string? Description, bool Hidden, string? IconUrl, decimal? Percent);

/// <summary>A game's newest news items, newest first, with a plain-text excerpt.</summary>
public record GetGameNewsQuery(int AppId, int Limit) : IRequest<List<GameNewsDto>>;

public record GameNewsDto(string Title, string? Url, string? Author, string? FeedLabel, DateTime PublishedAt, string Excerpt);
