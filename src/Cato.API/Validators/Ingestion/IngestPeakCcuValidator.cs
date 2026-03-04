using Cato.API.Models.Ingestion;
using FluentValidation;

namespace Cato.API.Validators.Ingestion;

public class IngestPeakCcuValidator : AbstractValidator<IngestPeakCcuCommand>
{
    public IngestPeakCcuValidator()
    {
        RuleFor(x => x.AppId).GreaterThan(0);
        RuleFor(x => x.FilePath).NotEmpty();
    }
}
