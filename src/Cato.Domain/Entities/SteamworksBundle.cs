namespace Cato.Domain.Entities;

/// <summary>
/// A Steam bundle id and its name, from the financial API's
/// <c>reference_data.bundle_info</c> (sale lines carry only the id).
/// </summary>
public class SteamworksBundle
{
    public int BundleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
