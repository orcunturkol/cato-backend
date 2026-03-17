using Cato.Domain.Entities;

namespace Cato.API.DTOs;

public record GameDto(
    Guid Id,
    int AppId,
    string Name,
    string GameType,
    DateOnly? ReleaseDate,
    decimal? PriceUsd,
    int DiscountPercent,
    string? DeveloperName,
    string? PublisherName,
    bool IsEarlyAccess,
    bool IsReleased,
    string? HeaderImageUrl,
    string? ShortDescription,
    string? SteamReviewScore,
    int ReviewCount,
    int FollowersCount,
    List<GenreDto> Genres,
    List<TagDto> Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record GenreDto(string Name, string Type, string Source);
public record TagDto(string Name, string Type, int Weight, string Source);

public record GroupMemberCountDto(
    Guid Id,
    DateOnly SnapshotDate,
    int MemberCount,
    string? Error,
    DateTime ScrapedAt
);

public record SteamDbSnapshotDto(
    Guid Id,
    DateOnly SnapshotDate,
    string Source,
    int Rank,
    string? Price,
    string? Rating,
    string? Release,
    int Follows,
    int SevenDayGain,
    DateTime ScrapedAt
);

public record SteamDbRankingDto(
    Guid SnapshotId, int AppId, Guid GameId, string GameName,
    string? HeaderImageUrl, DateOnly SnapshotDate, string Source,
    int Rank, string? Price, string? Rating, string? Release,
    int Follows, int SevenDayGain, DateTime ScrapedAt
);

public static class GameMappings
{
    public static GameDto ToDto(this Game game) => new(
        Id: game.Id,
        AppId: game.AppId,
        Name: game.Name,
        GameType: game.GameType,
        ReleaseDate: game.ReleaseDate,
        PriceUsd: game.PriceUsd,
        DiscountPercent: game.DiscountPercent,
        DeveloperName: game.Developer?.Name,
        PublisherName: game.Publisher?.Name,
        IsEarlyAccess: game.IsEarlyAccess,
        IsReleased: game.IsReleased,
        HeaderImageUrl: game.HeaderImageUrl,
        ShortDescription: game.ShortDescription,
        SteamReviewScore: game.SteamReviewScore,
        ReviewCount: game.ReviewCount,
        FollowersCount: game.FollowersCount,
        Genres: game.Genres.Select(g => new GenreDto(g.GenreName, g.GenreType, g.Source)).ToList(),
        Tags: game.Tags.Select(t => new TagDto(t.TagName, t.TagType, t.Weight, t.Source)).ToList(),
        CreatedAt: game.CreatedAt,
        UpdatedAt: game.UpdatedAt
    );
}
