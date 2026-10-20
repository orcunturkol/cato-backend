namespace Cato.Domain.Entities;

/// <summary>
/// One country of the Steamworks players page's "Players by region - Yesterday"
/// table: the country's share of a game's players on <c>SnapshotDate</c>. Steam shows
/// only yesterday, so history starts with the daily job; shares, not counts.
/// </summary>
public class SteamworksPlayerCountry
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public decimal SharePercent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
