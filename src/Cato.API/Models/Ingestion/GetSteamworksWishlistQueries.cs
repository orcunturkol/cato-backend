using MediatR;

namespace Cato.API.Models.Ingestion;

/// <summary>Daily Steamworks wishlist actions, oldest first. A day with no row had no activity.</summary>
public record GetSteamworksWishlistDailyQuery(int AppId, DateOnly? From, DateOnly? To) : IRequest<List<SteamworksWishlistDailyDto>>;

public record SteamworksWishlistDailyDto(
    DateOnly DateLocal,
    int Adds,
    int Deletes,
    int PurchasesAndActivations,
    int Gifts);

/// <summary>Steamworks wishlist cohort conversions, oldest first. A missing pair had no conversions.</summary>
public record GetSteamworksWishlistCohortsQuery(int AppId, DateOnly? From, DateOnly? To) : IRequest<List<SteamworksWishlistCohortDto>>;

public record SteamworksWishlistCohortDto(
    DateOnly DateLocal,
    DateOnly MonthCohort,
    int PurchasesAndActivations,
    int Gifts,
    int TotalConversions);

/// <summary>
/// One row per game with Steamworks wishlist data. The "last 30 days" window ends at
/// the latest DateLocal in the table, the same anchor the per-game view uses.
/// </summary>
public record GetSteamworksWishlistSummaryQuery : IRequest<SteamworksWishlistSummaryDto>;

public record SteamworksWishlistSummaryDto(
    DateOnly? LatestDate,
    List<SteamworksWishlistGameSummaryDto> Games);

public record SteamworksWishlistGameSummaryDto(
    Guid GameId,
    int AppId,
    string Name,
    string GameType,
    string? HeaderImageUrl,
    DateOnly FirstDate,
    DateOnly LastDate,
    int Adds,
    int Deletes,
    int PurchasesAndActivations,
    int Gifts,
    // Adds − deletes − purchases − gifts over all rows.
    int Outstanding,
    int Adds30d,
    int Deletes30d,
    int Conversions30d);

