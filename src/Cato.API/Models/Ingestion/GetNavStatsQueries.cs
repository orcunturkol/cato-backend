using MediatR;

namespace Cato.API.Models.Ingestion;

// Steamworks publisher Nav Stats (steamworks_nav_stat), all regions. A period is
// PeriodStart .. PeriodEnd (GMT, inclusive): one day from the daily job, a whole
// month from the history backfill. A range includes the periods that lie entirely
// inside it.

/// <summary>
/// Store page traffic of every product of a publisher over a range: totals per
/// traffic source, "all" totals per period (oldest first) and per product (most
/// visits first). Without a publisher id, the publisher with the latest data.
/// </summary>
public record GetNavStatsQuery(int? PublisherId, DateOnly? From, DateOnly? To) : IRequest<NavStatsDto>;

public record NavStatsDto(
    int? PublisherId,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    // First one-day period; before it the history is monthly.
    DateOnly? FirstDailyDate,
    // "all", then its parts "store", "steamworks" (Steam platform) and "external".
    List<NavStatsTotalDto> Totals,
    List<NavStatsPeriodDto> Periods,
    List<NavStatsProductDto> Products);

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
