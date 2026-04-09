using Cato.API.Models.Ingestion;
using FluentValidation;

namespace Cato.API.Validators.Ingestion;

public class IngestOwnedGameDataValidator : AbstractValidator<IngestOwnedGameDataCommand>
{
    public IngestOwnedGameDataValidator()
    {
        RuleFor(x => x.FileName).NotEmpty();
    }
}
