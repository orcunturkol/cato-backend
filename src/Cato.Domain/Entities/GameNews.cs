namespace Cato.Domain.Entities;

public class GameNews
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string ExternalId { get; set; } = string.Empty; // gid for news, URL id for patch_notes
    public string Source { get; set; } = string.Empty;     // "news" | "patch_notes"
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Author { get; set; }
    public string? Contents { get; set; }                  // BBCode (news) or HTML (patch_notes)
    public DateTime PublishedAt { get; set; }
    public string? FeedLabel { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
