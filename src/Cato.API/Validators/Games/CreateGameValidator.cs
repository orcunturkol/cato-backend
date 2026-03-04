using Cato.API.Models.Games;
using FluentValidation;

namespace Cato.API.Validators.Games;

public class CreateGameValidator : AbstractValidator<CreateGameCommand>
{
    public CreateGameValidator()
    {
        RuleFor(x => x.AppId).GreaterThan(0).WithMessage("AppId must be a positive integer.");
    }
}
