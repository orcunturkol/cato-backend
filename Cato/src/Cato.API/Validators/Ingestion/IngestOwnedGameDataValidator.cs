using Cato.API.Models.Ingestion;
using FluentValidation;

namespace Cato.API.Validators.Ingestion;

public class IngestOwnedGameDataValidator : AbstractValidator<IngestOwnedGameDataCommand>
{
    public IngestOwnedGameDataValidator()
    {
        RuleFor(x => x.AppId).GreaterThan(0);
        RuleFor(x => x.FilePath).NotEmpty();
    }
}
