using System.Text.Json;

namespace Cato.Domain.Entities;

public class MarketingTarget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TargetType { get; set; } = "Influencer"; // Influencer, Media, Event, MailingList
    public string? ContactEmail { get; set; }
    public string? ContactTwitter { get; set; }
    public string? ContactDiscord { get; set; }
    public JsonDocument? PreferredGenres { get; set; } // JSONB: ["FPS", "Strategy"]
    public JsonDocument? PreferredTags { get; set; } // JSONB: ["Roguelike", "Co-op"]
    public int? AudienceSize { get; set; }
    public string? AudienceRegion { get; set; }
    public string? Platform { get; set; } // Twitch, YouTube, Twitter, Event, etc.
    public decimal? EngagementRate { get; set; }
    public decimal? CostEstimateUsd { get; set; }
    public DateOnly? LastContacted { get; set; }
    public decimal? ResponseRate { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<ActionTarget> ActionTargets { get; set; } = [];
    public ICollection<TargetMatch> TargetMatches { get; set; } = [];
}
