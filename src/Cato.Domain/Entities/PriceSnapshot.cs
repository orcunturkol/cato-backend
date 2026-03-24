namespace Cato.Domain.Entities;

public class PriceSnapshot
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateTime CapturedAt { get; set; }
    public decimal BasePriceUsd { get; set; }
    public decimal FinalPriceUsd { get; set; }
    public int DiscountPercent { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; }

    public Game Game { get; set; } = null!;
}
