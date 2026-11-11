namespace Cato.Domain.Entities;

/// <summary>
/// A Steamworks key request, from the financial API's
/// <c>reference_data.key_request_info</c> (sale lines carry only the id). Steam
/// reports free-license claims ("add to library") of free products as activations
/// under a "Synthetic request" with no game code or territory; those rows are
/// <see cref="IsFreeLicense"/>, so the sales read side can count them apart from
/// retail key activations.
/// </summary>
public class SteamKeyRequest
{
    public int KeyRequestId { get; set; }
    public string? Notes { get; set; }
    public string? GameCodeDescription { get; set; }
    public string? TerritoryDescription { get; set; }
    public bool IsFreeLicense { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
