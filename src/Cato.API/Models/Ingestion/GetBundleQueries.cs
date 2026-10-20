using MediatR;

namespace Cato.API.Models.Ingestion;

/// <summary>
/// Every bundle with sales in the range (default: all time), largest gross sales first.
/// Figures are the publisher's share of each bundle; returns are positive.
/// </summary>
public record GetBundlesQuery(DateOnly? From, DateOnly? To) : IRequest<BundlesDto>;

public record BundlesDto(DateOnly? LatestDate, List<BundleSummaryDto> Bundles);

public record BundleSummaryDto(
    int BundleId,
    string? Name,
    int UnitsSold,
    int UnitsReturned,
    decimal GrossSalesUsd,
    decimal GrossReturnsUsd,
    decimal DiscountUsd,
    DateOnly FirstDate,
    DateOnly LastDate);

/// <summary>One bundle over the range: its day rows (oldest first) and its packages.</summary>
public record GetBundleQuery(int BundleId, DateOnly? From, DateOnly? To) : IRequest<BundleDetailDto>;

public record BundleDetailDto(int BundleId, string? Name, List<BundleDayDto> Days, List<BundlePackageDto> Packages);

public record BundleDayDto(DateOnly Date, int UnitsSold, int UnitsReturned, decimal GrossSalesUsd, decimal GrossReturnsUsd, decimal DiscountUsd);

// The game is found through the package's lines in the financial data; null when unknown.
public record BundlePackageDto(
    int PackageId,
    int? AppId,
    string? GameName,
    Guid? GameId,
    int UnitsSold,
    int UnitsReturned,
    decimal GrossSalesUsd,
    decimal GrossReturnsUsd);
