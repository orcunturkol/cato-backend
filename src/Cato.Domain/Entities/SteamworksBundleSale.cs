namespace Cato.Domain.Entities;

/// <summary>
/// One day of a Steam bundle's sales (Steamworks <c>QueryBundleSalesForCSV</c>), for the
/// publisher's share of the bundle. <c>DateLocal</c> is the partner site's report day.
/// Steam reports returns as negative numbers; they are stored positive.
/// </summary>
public class SteamworksBundleSale
{
    public Guid Id { get; set; }
    public int BundleId { get; set; }
    public int PartnerId { get; set; }
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
