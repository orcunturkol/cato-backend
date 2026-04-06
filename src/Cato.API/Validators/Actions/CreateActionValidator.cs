using Cato.API.Models.Actions;
using FluentValidation;

namespace Cato.API.Validators.Actions;

public class CreateActionValidator : AbstractValidator<CreateActionCommand>
{
    private static readonly string[] ValidActionTypes = ["Mailing", "Influencer", "Event", "Discount", "Bundle", "PR", "Advertisement"];
    private static readonly string[] ValidDecisionSources = ["Manual", "Rule", "AI", "Automated"];
    private static readonly string[] ValidStatuses = ["Planned", "Outreach", "Negotiating", "Scheduled", "Executed", "Completed", "Cancelled", "Failed"];

    public CreateActionValidator()
    {
        RuleFor(x => x.ActionType).NotEmpty().Must(t => ValidActionTypes.Contains(t))
            .WithMessage($"ActionType must be one of: {string.Join(", ", ValidActionTypes)}");
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.DecisionSource).Must(s => ValidDecisionSources.Contains(s!))
            .When(x => x.DecisionSource is not null)
            .WithMessage($"DecisionSource must be one of: {string.Join(", ", ValidDecisionSources)}");
        RuleFor(x => x.Status).Must(s => ValidStatuses.Contains(s!))
            .When(x => x.Status is not null)
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");
        RuleFor(x => x.BudgetUsd).GreaterThanOrEqualTo(0).When(x => x.BudgetUsd.HasValue);
    }
}
