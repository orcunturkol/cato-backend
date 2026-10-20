namespace Cato.Domain.Entities;

public class ActiveUsersHistory
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateTime RecordedAt { get; set; }
    public int Dau { get; set; }
    public int Mau { get; set; }
    // From the Steamworks players page; null for rows of the manual upload.
    public int? PeakConcurrentUsers { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
