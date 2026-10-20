using MediatR;

namespace Cato.API.Models.Ingestion;

/// <summary>
/// A game's demo downloads by region and by country over a range (most downloads
/// first). A period is SnapshotDate .. PeriodEnd: one day from the daily job, a whole
/// month from the history backfill; a range includes the periods that lie entirely
/// inside it. Total is the sum of the region rows.
/// </summary>
public record GetDemoDownloadsQuery(int AppId, DateOnly? From, DateOnly? To) : IRequest<DemoDownloadsDto>;

public record DemoDownloadsDto(
    int? DemoAppId,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    // First one-day period; before it the history is monthly.
    DateOnly? FirstDailyDate,
    long Total,
    List<DemoDownloadGeoDto> Regions,
    List<DemoDownloadGeoDto> Countries);

public record DemoDownloadGeoDto(string Name, long Downloads);
