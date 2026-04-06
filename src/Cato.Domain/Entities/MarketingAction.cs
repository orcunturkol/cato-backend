namespace Cato.Domain.Entities;

// Named MarketingAction to avoid conflict with System.Action
// Maps to DB table: action
public class MarketingAction
{
    public Guid Id { get; set; }
    public string ActionType { get; set; } = "Mailing"; // Mailing, Influencer, Event, Discount, Bundle, PR, Advertisement
    public string DecisionSource { get; set; } = "Manual"; // Manual, Rule, AI, Automated
    public string Status { get; set; } = "Planned"; // Planned, Outreach, Negotiating, Scheduled, Executed, Completed, Cancelled, Failed
    public DateOnly? PlannedDate { get; set; }
    public DateOnly? ActionDate { get; set; }
    public DateOnly? CompletionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? BudgetUsd { get; set; }
    public decimal? ActualCostUsd { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<GameAction> GameActions { get; set; } = [];
    public ICollection<ActionTarget> ActionTargets { get; set; } = [];
    public ActionImpact? Impact { get; set; }
}
