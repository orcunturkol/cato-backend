using Cato.API.Models.Actions;
using FluentValidation;

namespace Cato.API.Validators.Actions;

public class UpdateActionTargetValidator : AbstractValidator<UpdateActionTargetCommand>
{
    private static readonly string[] ValidStatuses = ["Planned", "Contacted", "Responded", "Accepted", "Rejected", "Negotiating", "Live", "Completed", "Cancelled"];

    public UpdateActionTargetValidator()
    {
        RuleFor(x => x.ActionId).NotEmpty();
        RuleFor(x => x.TargetId).NotEmpty();
        RuleFor(x => x.Status).Must(s => ValidStatuses.Contains(s!))
            .When(x => x.Status is not null)
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");
        RuleFor(x => x.CostUsd).GreaterThanOrEqualTo(0).When(x => x.CostUsd.HasValue);
    }
}
