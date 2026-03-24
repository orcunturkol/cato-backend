namespace Cato.Domain.Entities;

public class AppKeyValueSnapshot
{
    public Guid Id { get; set; }
    public int AppId { get; set; }
    public Guid? GameId { get; set; }
    public long ChangeNumber { get; set; }
    public string RawKeyValuesJson { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Game? Game { get; set; }
}
