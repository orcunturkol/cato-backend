using Cato.API.Models.Ingestion;
using FluentValidation;

namespace Cato.API.Validators.Ingestion;

public class IngestWishlistInsightsValidator : AbstractValidator<IngestWishlistInsightsCommand>
{
    public IngestWishlistInsightsValidator()
    {
        RuleFor(x => x.AppId).GreaterThan(0);
        RuleFor(x => x.FilePath).NotEmpty();
    }
}
