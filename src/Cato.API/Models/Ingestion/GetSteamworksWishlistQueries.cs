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
/// Steamworks wishlist actions by country, summed over the range (both ends
/// inclusive), most adds first. <c>FirstDate</c>/<c>LastDate</c> are the game's
/// whole by-country coverage, not the range: the collector sends a short recent
/// window each day, so older ranges may have no country rows at all.
/// </summary>
public record GetSteamworksWishlistCountriesQuery(int AppId, DateOnly? From, DateOnly? To) : IRequest<SteamworksWishlistCountriesDto>;

public record SteamworksWishlistCountriesDto(
    DateOnly? FirstDate,
    DateOnly? LastDate,
    List<SteamworksWishlistCountryTotalDto> Countries);

public record SteamworksWishlistCountryTotalDto(
    string Country,
    string Region,
    int Adds,
    int Deletes,
    int PurchasesAndActivations,
    int Gifts);

/// <summary>
/// One row per game with Steamworks wishlist data. The "last 30 days" window ends at
/// the latest DateLocal in the table, the same anchor the per-game view uses.
/// </summary>
public record GetSteamworksWishlistSummaryQuery : IRequest<SteamworksWishlistSummaryDto>;

public record SteamworksWishlistSummaryDto(
    DateOnly? LatestDate,
    List<SteamworksWishlistGameSummaryDto> Games,
    // Per partner (by partner id): the sums over the apps it lists, as on the Steamworks
    // "Lifetime Wishlist Summary" page. AllPartners counts each app once.
    List<SteamworksWishlistPartnerSummaryDto> Partners,
    SteamworksWishlistTotalsDto AllPartners);

/// <summary>Lifetime totals of a set of apps. AppCount includes apps with no wishlist data.</summary>
public record SteamworksWishlistTotalsDto(
    int AppCount,
    int Adds,
    int Deletes,
    int PurchasesAndActivations,
    int Gifts,
    int Outstanding);

public record SteamworksWishlistPartnerSummaryDto(
    int PartnerId,
    string Name,
    int AppCount,
    int Adds,
    int Deletes,
    int PurchasesAndActivations,
    int Gifts,
    int Outstanding,
    // The partner's apps (steamworks_partner_app), ascending, so a page can filter games by partner.
    List<int> AppIds);

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

