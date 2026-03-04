using Cato.API.Models.Games;
using FluentValidation;

namespace Cato.API.Validators.Games;

public class UpdateGameValidator : AbstractValidator<UpdateGameCommand>
{
    public UpdateGameValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        When(x => x.PriceUsd.HasValue, () =>
            RuleFor(x => x.PriceUsd!.Value).GreaterThanOrEqualTo(0));
    }
}
