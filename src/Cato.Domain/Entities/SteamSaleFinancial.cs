namespace Cato.Domain.Entities;

/// <summary>
/// One line of Steam's <c>GetDetailedSales</c> report, stored under the game of its
/// primary app. Steam sends several lines that share date, country, package and
/// platform (standalone vs bundle, different discounts or prices), so there is no
/// business key: ingestion replaces all of a game's lines for a date at once.
/// </summary>
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
    // The line's own app; differs from the game's app for DLC and soundtracks,
    // which Steam groups under their primary app.
    public int? AppId { get; set; }
    public int? PrimaryAppId { get; set; }
    public int? BundleId { get; set; }
    public int? PartnerId { get; set; }
    public string? LineItemType { get; set; }
    public int? KeyRequestId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
