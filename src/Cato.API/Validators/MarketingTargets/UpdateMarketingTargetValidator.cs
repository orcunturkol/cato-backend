using Cato.API.Models.MarketingTargets;
using FluentValidation;

namespace Cato.API.Validators.MarketingTargets;

public class UpdateMarketingTargetValidator : AbstractValidator<UpdateMarketingTargetCommand>
{
    private static readonly string[] ValidTargetTypes = ["Influencer", "Media", "Event", "MailingList"];

    public UpdateMarketingTargetValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(500).When(x => x.Name is not null);
        RuleFor(x => x.TargetType).Must(t => ValidTargetTypes.Contains(t!))
            .When(x => x.TargetType is not null)
            .WithMessage($"TargetType must be one of: {string.Join(", ", ValidTargetTypes)}");
        RuleFor(x => x.ContactEmail).EmailAddress().When(x => x.ContactEmail is not null);
        RuleFor(x => x.EngagementRate).InclusiveBetween(0, 100).When(x => x.EngagementRate.HasValue);
        RuleFor(x => x.ResponseRate).InclusiveBetween(0, 100).When(x => x.ResponseRate.HasValue);
        RuleFor(x => x.CostEstimateUsd).GreaterThanOrEqualTo(0).When(x => x.CostEstimateUsd.HasValue);
        RuleFor(x => x.AudienceSize).GreaterThanOrEqualTo(0).When(x => x.AudienceSize.HasValue);
    }
}
