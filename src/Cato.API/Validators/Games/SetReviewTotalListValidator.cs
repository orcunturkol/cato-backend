using Cato.API.Models.Games;
using FluentValidation;

namespace Cato.API.Validators.Games;

public class SetReviewTotalListValidator : AbstractValidator<SetReviewTotalListCommand>
{
    public const int MaxAppIds = 20_000;

    public SetReviewTotalListValidator()
    {
        RuleFor(x => x.AppIds).NotNull().WithMessage("appIds is required (an empty list clears it).");
        RuleFor(x => x.AppIds.Count).LessThanOrEqualTo(MaxAppIds).When(x => x.AppIds is not null)
            .WithMessage($"Pass at most {MaxAppIds:N0} appIds.");
        RuleForEach(x => x.AppIds).GreaterThan(0).WithMessage("Every AppId must be a positive integer.");
    }
}
