using Cato.API.Models.JobRuns;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;
using FluentValidation;

namespace Cato.API.Validators.JobRuns;

public class PublishJobScheduleValidator : AbstractValidator<PublishJobScheduleCommand>
{
    private const int MaxEntries = 200;
    private const int MaxTimeoutSeconds = 7 * 24 * 3600;

    public PublishJobScheduleValidator()
    {
        RuleFor(x => x.Producer).NotEmpty().MaximumLength(50)
            .NotEqual(JobRunProducer.CatoBackend)
            .WithMessage("The backend schedule comes from its own settings and cannot be published.");
        RuleFor(x => x.Source).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SourceHost).MaximumLength(255);
        RuleFor(x => x.Entries).NotNull()
            .Must(e => e.Count <= MaxEntries).WithMessage($"At most {MaxEntries} entries.");

        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.JobName).NotEmpty().MaximumLength(150);
            entry.RuleFor(e => e.Label).MaximumLength(200);
            entry.RuleFor(e => e.Note).MaximumLength(500);
            entry.RuleFor(e => e.Cron)
                .Must(c => c is null || JobNextRunCalculator.TryParseCron(c, out _))
                .WithMessage(e => $"'{e.Cron}' is not a 5-field cron expression.");
            entry.RuleFor(e => e.Cron).NotEmpty().When(e => e.Enabled)
                .WithMessage("An enabled entry needs a cron expression.");
            entry.RuleFor(e => e.TimeoutSeconds).InclusiveBetween(1, MaxTimeoutSeconds)
                .When(e => e.TimeoutSeconds is not null);
        });
    }
}
