using MediatR;

namespace Cato.API.Models.Ingestion;

// Steamworks publisher Nav Stats (steamworks_nav_stat), all regions. A period is
// PeriodStart .. PeriodEnd (GMT, inclusive): one day from the daily job, a whole
// month from the history backfill. A range includes the periods that lie entirely
// inside it.

/// <summary>
/// Store page traffic of every product over a range: totals per traffic source, "all"
/// totals per period (oldest first) and per product (most visits first).
/// <c>PublisherId</c> null or 0 (the default) means ALL organizations: each product (app
/// id) is read from ONE publisher, the one with the most "all" visits for it in the range
/// (tie: lowest id), with all its attributions and periods. Publishers are never summed,
/// because the same product can sit under two of them with identical numbers but different
/// coverage (monthly vs daily rows); the response then has PublisherId = null. A positive
/// <c>PublisherId</c> shows that publisher alone, as before.
/// </summary>
public record GetNavStatsQuery(int? PublisherId, DateOnly? From, DateOnly? To) : IRequest<NavStatsDto>;

public record NavStatsDto(
    // The publisher shown; null for all organizations.
    int? PublisherId,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    // First one-day period; before it the history is monthly.
    DateOnly? FirstDailyDate,
    // "all", then its parts "store", "steamworks" (Steam platform) and "external".
    List<NavStatsTotalDto> Totals,
    List<NavStatsPeriodDto> Periods,
    List<NavStatsProductDto> Products,
    // Every publisher with nav stats, for the partner selector (name from steamworks_partner).
    List<NavStatsPublisherDto> Publishers);

public record NavStatsPublisherDto(int PublisherId, string? Name);

public record NavStatsTotalDto(string Attribution, long Impressions, long Visits);

public record NavStatsPeriodDto(DateOnly PeriodStart, DateOnly PeriodEnd, long Impressions, long Visits);

public record NavStatsProductDto(
    int AppId,
    string? Name,
    // The CATO game for this app; null for most demos and playtests.
    Guid? GameId,
    long Impressions,
    long Visits,
    long StoreImpressions,
    long StoreVisits,
    long PlatformImpressions,
    long PlatformVisits,
    long ExternalImpressions,
    long ExternalVisits);
