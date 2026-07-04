using System.Text.Json;

namespace Cato.Domain.Entities;

/// <summary>
/// A Steam storefront special event (sale page / fest), scraped from the
/// partner-event data embedded in store pages. Rows are upserted by
/// <see cref="AnnouncementGid"/>; "still listed" is judged by LastSeenAt
/// recency at query time — rows are never deleted or flagged inactive.
/// </summary>
public class SteamSpecialEvent
{
    public Guid Id { get; set; }
    public string AnnouncementGid { get; set; } = string.Empty; // Steam's event id; natural key
    public string? SaleVanityId { get; set; }                   // e.g. "OPENGAMEFEST2026"
    public string EventUrl { get; set; } = string.Empty;
    public long ClanAccountId { get; set; }
    public int EventType { get; set; }                          // 20 = sale event
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Description { get; set; }                    // bbcode
    public string? HeaderImageUrl { get; set; }
    public string? LogoImageUrl { get; set; }
    public string? CapsuleImageUrl { get; set; }
    public string? BackgroundColor { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public JsonDocument? TabNames { get; set; }                 // JSONB: hub tab ids/labels the event appeared under
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<SteamSpecialEventGame> Games { get; set; } = [];
}
