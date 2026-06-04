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

    public bool VotedUp { get; set; }
    public string Language { get; set; } = string.Empty;
    public string ReviewText { get; set; } = string.Empty;

    /// <summary>Total lifetime playtime of the reviewer in minutes.</summary>
    public int PlaytimeForeverMinutes { get; set; }

    /// <summary>Playtime at the time the review was written, in minutes.</summary>
    public int PlaytimeAtReviewMinutes { get; set; }

    public int VotesUp { get; set; }
    public int VotesFunny { get; set; }

    public bool SteamPurchase { get; set; }
    public bool ReceivedForFree { get; set; }
    public bool WrittenDuringEarlyAccess { get; set; }

    /// <summary>UTC datetime when the review was originally posted on Steam.</summary>
    public DateTime TimestampCreated { get; set; }

    /// <summary>UTC datetime of the last edit to this review on Steam.</summary>
    public DateTime TimestampUpdated { get; set; }

    public DateTime CreatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
