namespace Cato.API.Services;

/// <summary>
/// Payload of a <c>game.analyzed.*</c> event, published by the reddit_metrics
/// pipeline when Gemini extracts real metrics for a game and the app ID survives
/// verification against the Steam store.
/// </summary>
public record GameAnalyzedMessage
{
    public int AppId { get; init; }
    public string? GameName { get; init; }

    /// <summary>Reddit post the metrics came from — carried for traceability.</summary>
    public string? RedditId { get; init; }

    /// <summary>
    /// When the extraction ran. Becomes the queue priority: more recent analysis
    /// sorts earlier within the prioritised band.
    /// </summary>
    public DateTimeOffset ExtractedAt { get; init; }

    public string Source { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
}
