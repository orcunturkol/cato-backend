using System.Text.Json;

namespace Cato.Domain.Entities;

public class TargetMatch
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid TargetId { get; set; }
    public string? LifecycleStage { get; set; } // Pre-launch, Launch, Early Access, Post-launch, Live, Sunset
    public decimal? RelevanceScore { get; set; }
    public decimal? GenreMatchScore { get; set; }
    public decimal? TagMatchScore { get; set; }
    public decimal? HistoricalPerformanceScore { get; set; }
    public int SampleSize { get; set; }
    public JsonDocument? MatchingGenres { get; set; } // JSONB: ["FPS", "Action"]
    public JsonDocument? MatchingTags { get; set; } // JSONB: ["Multiplayer", "Co-op"]
    public DateTime? CalculatedAt { get; set; }

    // Navigation properties
    public Game Game { get; set; } = null!;
    public MarketingTarget Target { get; set; } = null!;
}
