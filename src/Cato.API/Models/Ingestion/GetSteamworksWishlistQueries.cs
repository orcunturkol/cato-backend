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
