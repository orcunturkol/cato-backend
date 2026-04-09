namespace Cato.Domain.Entities;

public class ActiveUsersHistory
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateTime RecordedAt { get; set; }
    public int Dau { get; set; }
    public int Mau { get; set; }
    public DateTime CreatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
