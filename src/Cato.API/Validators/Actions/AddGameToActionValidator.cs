using Cato.API.Models.Actions;
using FluentValidation;

namespace Cato.API.Validators.Actions;

public class AddGameToActionValidator : AbstractValidator<AddGameToActionCommand>
{
    private static readonly string[] ValidRoles = ["Primary", "Secondary", "Featured", "Included"];

    public AddGameToActionValidator()
    {
        RuleFor(x => x.ActionId).NotEmpty();
        RuleFor(x => x.GameId).NotEmpty();
        RuleFor(x => x.GameRole).Must(r => ValidRoles.Contains(r!))
            .When(x => x.GameRole is not null)
            .WithMessage($"GameRole must be one of: {string.Join(", ", ValidRoles)}");
    }
}
