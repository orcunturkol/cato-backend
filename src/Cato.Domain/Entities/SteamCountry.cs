namespace Cato.Domain.Entities;

/// <summary>
/// Steam's country list as the financial API reports it (<c>reference_data.country_info</c>):
/// the code used on sale lines, its name and Steam's region. Kept up to date by the
/// financial ingestion, so sales can be grouped by region.
/// </summary>
public class SteamCountry
{
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
