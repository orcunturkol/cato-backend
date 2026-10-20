namespace Cato.Domain.Entities;

/// <summary>
/// One day of one package's sales inside a Steam bundle (Steamworks
/// <c>QueryBundlePackageSalesForCSV</c>). Returns are stored positive.
/// </summary>
public class SteamworksBundlePackageSale
{
    public Guid Id { get; set; }
    public int BundleId { get; set; }
    public int PackageId { get; set; }
    public DateOnly DateLocal { get; set; }
    public int UnitsSold { get; set; }
    public int UnitsReturned { get; set; }
    public decimal GrossSalesUsd { get; set; }
    public decimal GrossReturnsUsd { get; set; }
    public decimal DiscountUsd { get; set; }
    public decimal DiscountReturnsUsd { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
