using Cato.API.Models.Ingestion;
using FluentValidation;

namespace Cato.API.Validators.Ingestion;

public class IngestStoreTrafficValidator : AbstractValidator<IngestStoreTrafficCommand>
{
    public IngestStoreTrafficValidator()
    {
        RuleFor(x => x.AppId).GreaterThan(0);
        RuleFor(x => x.FilePath).NotEmpty();
    }
}
