namespace Cato.Domain.Entities;

public class ActionTarget
{
    public Guid Id { get; set; }
    public Guid ActionId { get; set; }
    public Guid TargetId { get; set; }
    public DateOnly? OutreachDate { get; set; }
    public DateOnly? ResponseDate { get; set; }
    public string Status { get; set; } = "Planned"; // Planned, Contacted, Responded, Accepted, Rejected, Negotiating, Live, Completed, Cancelled
    public string? DeliverableUrl { get; set; }
    public int? Views { get; set; }
    public int? Engagement { get; set; }
    public decimal? CostUsd { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public MarketingAction Action { get; set; } = null!;
    public MarketingTarget Target { get; set; } = null!;
}
