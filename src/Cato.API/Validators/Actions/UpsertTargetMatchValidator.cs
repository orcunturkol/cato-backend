using Cato.API.Models.Actions;
using FluentValidation;

namespace Cato.API.Validators.Actions;

public class UpsertTargetMatchValidator : AbstractValidator<UpsertTargetMatchCommand>
{
    private static readonly string[] ValidStages = ["Pre-launch", "Launch", "Early Access", "Post-launch", "Live", "Sunset"];

    public UpsertTargetMatchValidator()
    {
        RuleFor(x => x.GameId).NotEmpty();
        RuleFor(x => x.TargetId).NotEmpty();
        RuleFor(x => x.LifecycleStage).Must(s => ValidStages.Contains(s!))
            .When(x => x.LifecycleStage is not null)
            .WithMessage($"LifecycleStage must be one of: {string.Join(", ", ValidStages)}");
        RuleFor(x => x.RelevanceScore).InclusiveBetween(0, 100).When(x => x.RelevanceScore.HasValue);
        RuleFor(x => x.GenreMatchScore).InclusiveBetween(0, 100).When(x => x.GenreMatchScore.HasValue);
        RuleFor(x => x.TagMatchScore).InclusiveBetween(0, 100).When(x => x.TagMatchScore.HasValue);
        RuleFor(x => x.HistoricalPerformanceScore).InclusiveBetween(0, 100).When(x => x.HistoricalPerformanceScore.HasValue);
    }
}
