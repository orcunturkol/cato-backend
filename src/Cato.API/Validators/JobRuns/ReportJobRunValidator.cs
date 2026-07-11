using Cato.API.Models.JobRuns;
using Cato.Domain.Entities;
using FluentValidation;

namespace Cato.API.Validators.JobRuns;

public class ReportJobRunValidator : AbstractValidator<ReportJobRunCommand>
{
    private static readonly string[] ValidStatuses =
    [
        JobRunStatus.Running,
        JobRunStatus.Succeeded,
        JobRunStatus.PartialSuccess,
        JobRunStatus.Failed,
    ];

    public ReportJobRunValidator()
    {
        RuleFor(x => x.JobName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Producer).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Status).Must(s => ValidStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses)}");
        RuleFor(x => x.EndTime).GreaterThanOrEqualTo(x => x.StartTime)
            .WithMessage("EndTime must be on or after StartTime.");
    }
}
