namespace Cato.Domain.Entities;

public class SteamSaleFinancial
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SaleDate { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string Platform { get; set; } = "Steam";
    public int? PackageId { get; set; }
    public int SalesUnits { get; set; }
    public int ReturnsUnits { get; set; }
    public int NetUnits { get; set; } // Computed: sales_units - returns_units
    public decimal GrossRevenueUsd { get; set; }
    public decimal GrossReturnsUsd { get; set; }
    public decimal SteamCommissionUsd { get; set; }
    public decimal TaxUsd { get; set; }
    public decimal NetRevenueUsd { get; set; }
    public string? Currency { get; set; }
    public string? BasePrice { get; set; }
    public string? SalePrice { get; set; }
    public int? DiscountId { get; set; }
    public string? SaleType { get; set; }
    public int? CombinedDiscountId { get; set; }
    public int? RevenueShareTier { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
