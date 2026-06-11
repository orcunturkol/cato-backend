namespace Cato.Domain.Entities;

public class SteamReview
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }

    /// <summary>
    /// Steam recommendation ID — unique identifier for a single review.
    /// Used as dedup key: unique constraint on (GameId, RecommendationId).
    /// </summary>
    public string RecommendationId { get; set; } = string.Empty;

    /// <summary>
    /// SteamID64 of the review author. Nullable: rows ingested before this
    /// column existed have no value (forward-only enrichment).
    /// </summary>
    public long? AuthorSteamId { get; set; }

    public bool VotedUp { get; set; }
    public string Language { get; set; } = string.Empty;
    public string ReviewText { get; set; } = string.Empty;

    public int PlaytimeForeverMinutes { get; set; }

    public int PlaytimeAtReviewMinutes { get; set; }

    public int VotesUp { get; set; }
    public int VotesFunny { get; set; }

    public bool SteamPurchase { get; set; }
    public bool ReceivedForFree { get; set; }
    public bool WrittenDuringEarlyAccess { get; set; }

    public DateTime TimestampCreated { get; set; }

    public DateTime TimestampUpdated { get; set; }

    public DateTime CreatedAt { get; set; }

    // write-once; Steam-side edits are tracked via TimestampUpdated
    public Game Game { get; set; } = null!;
}
