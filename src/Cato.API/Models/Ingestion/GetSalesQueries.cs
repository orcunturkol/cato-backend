using MediatR;

namespace Cato.API.Models.Ingestion;

// Steam sales from steam_sale_financial (GetDetailedSales lines). "Net" is Steam's
// net_sales_usd: gross sales minus refunds and VAT, before Steam's revenue share.
// Units: gross = sold through Steam, returned = refunded (positive), net = gross − returned.
// Activated = retail/CD keys activated on Steam: not sales, no Steam revenue.

/// <summary>A game's sales per day, oldest first. A day with no row had no sales.</summary>
public record GetSalesDailyQuery(int AppId, DateOnly? From, DateOnly? To) : IRequest<List<SalesDailyDto>>;

public record SalesDailyDto(
    DateOnly Date,
    int GrossUnits,
    int ReturnedUnits,
    int NetUnits,
    int ActivatedUnits,
    decimal GrossUsd,
    decimal ReturnsUsd,
    decimal NetUsd);

/// <summary>
/// A game's sales per country over the range (both ends inclusive), most net revenue
/// first, with Steam's country name and region. FirstDate/LastDate: the game's whole
/// sales coverage.
/// </summary>
public record GetSalesCountriesQuery(int AppId, DateOnly? From, DateOnly? To) : IRequest<SalesCountriesDto>;

public record SalesCountriesDto(
    DateOnly? FirstDate,
    DateOnly? LastDate,
    List<SalesCountryDto> Countries);

public record SalesCountryDto(
    string CountryCode,
    string CountryName,
    string Region,
    int GrossUnits,
    int ReturnedUnits,
    int NetUnits,
    decimal GrossUsd,
    decimal NetUsd);

/// <summary>
/// One row per game with sales. The 30-day window ends at the latest sale date in
/// the table, like the wishlist summary.
/// </summary>
public record GetSalesSummaryQuery : IRequest<SalesSummaryDto>;

public record SalesSummaryDto(
    DateOnly? LatestDate,
    List<SalesGameSummaryDto> Games);

public record SalesGameSummaryDto(
    Guid GameId,
    int AppId,
    string Name,
    string GameType,
    string? HeaderImageUrl,
    DateOnly FirstDate,
    DateOnly LastDate,
    int GrossUnits,
    int ReturnedUnits,
    int NetUnits,
    int ActivatedUnits,
    decimal GrossUsd,
    decimal NetUsd,
    int NetUnits30d,
    decimal NetUsd30d);
