using Cato.API.Models.Games;
using FluentValidation;

namespace Cato.API.Validators.Games;

public class SyncOwnedGamesValidator : AbstractValidator<SyncOwnedGamesCommand>
{
    public SyncOwnedGamesValidator()
    {
        RuleFor(x => x.Games)
            .NotEmpty().WithMessage("Games must contain at least one entry.")
            .Must(games => games is null || games.Count <= 2000).WithMessage("Games may contain at most 2000 entries.");
        RuleForEach(x => x.Games).ChildRules(game =>
            game.RuleFor(g => g.AppId).GreaterThan(0).WithMessage("AppId must be a positive integer."));
    }
}
