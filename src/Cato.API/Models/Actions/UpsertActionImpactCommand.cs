using Cato.API.DTOs;
using MediatR;

namespace Cato.API.Models.Actions;

public record UpsertActionImpactCommand(
    Guid ActionId,
    DateOnly? MeasurementStart,
    DateOnly? MeasurementEnd,
    DateOnly? BaselineStart,
    DateOnly? BaselineEnd,
    int? BaselineWishlistAdds,
    int? ResultWishlistAdds,
    int? WishlistChange,
    decimal? WishlistChangePercent,
    int? BaselineSalesUnits,
    int? ResultSalesUnits,
    int? SalesUnitsChange,
    decimal? SalesChangePercent,
    decimal? BaselineRevenueUsd,
    decimal? ResultRevenueUsd,
    decimal? RevenueChangeUsd,
    decimal? RevenueChangePercent,
    int? BaselineTraffic,
    int? ResultTraffic,
    int? TrafficChange,
    decimal? TrafficChangePercent,
    decimal? BaselineConversionRate,
    decimal? ResultConversionRate,
    decimal? ConversionRateChange,
    decimal? TotalCostUsd,
    decimal? Roi,
    string? Notes,
    string? CalculatedBy
) : IRequest<Result<ActionImpactDto>>;
