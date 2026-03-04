namespace Cato.Domain.Entities;

public class GroupMemberCountSnapshot
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int MemberCount { get; set; }
    public string? Error { get; set; }
    public DateTime ScrapedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation
    public Game Game { get; set; } = null!;
}
