using MediatR;

namespace Cato.API.Models.Ingestion;

// Steamworks store traffic (steam_traffic_breakdown). A period is SnapshotDate ..
// PeriodEnd (GMT, inclusive): one day from the daily job, a whole month from the
// history backfill. A range includes the periods that lie entirely inside it.

/// <summary>
/// A game's store traffic totals per period, oldest first. Impressions and visits are
/// the sums of the non-Country rows; Visits leaves out Bot Traffic, which is BotVisits.
/// </summary>
public record GetStoreTrafficPeriodsQuery(int AppId, DateOnly? From, DateOnly? To) : IRequest<List<StoreTrafficPeriodDto>>;

public record StoreTrafficPeriodDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    long Impressions,
    long Visits,
    long BotVisits,
    long OwnerImpressions,
    long OwnerVisits);

/// <summary>
/// Where a game's store traffic came from over a range: page category/feature totals
/// (most visits first) and the country split (Steam's country code, name and region).
/// </summary>
public record GetStoreTrafficBreakdownQuery(int AppId, DateOnly? From, DateOnly? To) : IRequest<StoreTrafficBreakdownDto>;

public record StoreTrafficBreakdownDto(
    DateOnly? FirstDate,
    DateOnly? LastDate,
    // First one-day period; before it the history is monthly.
    DateOnly? FirstDailyDate,
    List<StoreTrafficSourceDto> Sources,
    List<StoreTrafficCountryDto> Countries);

public record StoreTrafficSourceDto(
    string Category,
    string Feature,
    long Impressions,
    long Visits,
    long OwnerImpressions,
    long OwnerVisits);

public record StoreTrafficCountryDto(
    string CountryCode,
    string CountryName,
    string Region,
    long Impressions,
    long Visits);
