namespace Cato.Domain.Entities;

public class ActionImpact
{
    public Guid Id { get; set; }
    public Guid ActionId { get; set; }
    public DateOnly? MeasurementStart { get; set; }
    public DateOnly? MeasurementEnd { get; set; }
    public DateOnly? BaselineStart { get; set; }
    public DateOnly? BaselineEnd { get; set; }

    // Wishlist impact
    public int? BaselineWishlistAdds { get; set; }
    public int? ResultWishlistAdds { get; set; }
    public int? WishlistChange { get; set; }
    public decimal? WishlistChangePercent { get; set; }

    // Sales impact
    public int? BaselineSalesUnits { get; set; }
    public int? ResultSalesUnits { get; set; }
    public int? SalesUnitsChange { get; set; }
    public decimal? SalesChangePercent { get; set; }

    // Revenue impact
    public decimal? BaselineRevenueUsd { get; set; }
    public decimal? ResultRevenueUsd { get; set; }
    public decimal? RevenueChangeUsd { get; set; }
    public decimal? RevenueChangePercent { get; set; }

    // Traffic impact
    public int? BaselineTraffic { get; set; }
    public int? ResultTraffic { get; set; }
    public int? TrafficChange { get; set; }
    public decimal? TrafficChangePercent { get; set; }

    // Conversion impact
    public decimal? BaselineConversionRate { get; set; }
    public decimal? ResultConversionRate { get; set; }
    public decimal? ConversionRateChange { get; set; }

    // ROI
    public decimal? TotalCostUsd { get; set; }
    public decimal? Roi { get; set; }

    public string? Notes { get; set; }
    public DateTime? CalculatedAt { get; set; }
    public string? CalculatedBy { get; set; }

    // Navigation properties
    public MarketingAction Action { get; set; } = null!;
}
