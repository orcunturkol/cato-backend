namespace Cato.Domain.Entities;

public class AppChangeRecord
{
    public Guid Id { get; set; }
    public int AppId { get; set; }
    public Guid? GameId { get; set; }
    public long ChangeNumber { get; set; }
    public string Section { get; set; } = string.Empty;
    public string KeyPath { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Game? Game { get; set; }
}
