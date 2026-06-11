using System.Text.Json.Serialization;

namespace Cato.Infrastructure.Steam.Models;

public class SteamAppReviewsResponse
{
    /// <summary>1 = success, 0 = error.</summary>
    [JsonPropertyName("success")]
    public int Success { get; set; }

    [JsonPropertyName("query_summary")]
    public SteamReviewQuerySummary QuerySummary { get; set; } = new();

    [JsonPropertyName("reviews")]
    public List<SteamReviewItem> Reviews { get; set; } = [];

    /// <summary>
    /// Opaque pagination cursor returned by Steam.
    /// Pass this value as the next request's cursor.
    /// When Steam returns "*" again (or the same cursor), there are no more pages.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; set; }
}

public class SteamReviewQuerySummary
{
    [JsonPropertyName("num_reviews")]
    public int NumReviews { get; set; }

    /// <summary>Steam numeric score 0–9.</summary>
    [JsonPropertyName("review_score")]
    public int ReviewScore { get; set; }

    /// <summary>e.g. "Very Positive", "Mixed", "Overwhelmingly Positive".</summary>
    [JsonPropertyName("review_score_desc")]
    public string? ReviewScoreDesc { get; set; }

    [JsonPropertyName("total_positive")]
    public int TotalPositive { get; set; }

    [JsonPropertyName("total_negative")]
    public int TotalNegative { get; set; }

    [JsonPropertyName("total_reviews")]
    public int TotalReviews { get; set; }
}

public class SteamReviewItem
{
    [JsonPropertyName("recommendationid")]
    public string? RecommendationId { get; set; }

    [JsonPropertyName("author")]
    public SteamReviewAuthor Author { get; set; } = new();

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("review")]
    public string? ReviewText { get; set; }

    [JsonPropertyName("timestamp_created")]
    public long TimestampCreated { get; set; }

    [JsonPropertyName("timestamp_updated")]
    public long TimestampUpdated { get; set; }

    [JsonPropertyName("voted_up")]
    public bool VotedUp { get; set; }

    [JsonPropertyName("votes_up")]
    public int VotesUp { get; set; }

    [JsonPropertyName("votes_funny")]
    public int VotesFunny { get; set; }

    [JsonPropertyName("steam_purchase")]
    public bool SteamPurchase { get; set; }

    [JsonPropertyName("received_for_free")]
    public bool ReceivedForFree { get; set; }

    [JsonPropertyName("written_during_early_access")]
    public bool WrittenDuringEarlyAccess { get; set; }
}

public class SteamReviewAuthor
{
    [JsonPropertyName("playtime_forever")]
    public int PlaytimeForever { get; set; }

    [JsonPropertyName("playtime_at_review")]
    public int PlaytimeAtReview { get; set; }
}
