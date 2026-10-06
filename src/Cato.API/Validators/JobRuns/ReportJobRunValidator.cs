using System.Text.Json;
using Cato.API.Models.JobRuns;
using Cato.Domain.Entities;
using FluentValidation;

namespace Cato.API.Validators.JobRuns;

public class ReportJobRunValidator : AbstractValidator<ReportJobRunCommand>
{
    public ReportJobRunValidator()
    {
        RuleFor(x => x.JobName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Producer).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Status).Must(s => JobRunStatus.Reportable.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", JobRunStatus.Reportable)}");
        RuleFor(x => x.StartTime).NotEqual(default(DateTime))
            .WithMessage("StartTime is required.");
        RuleFor(x => x.EndTime).NotNull()
            .When(x => x.Status != JobRunStatus.Running)
            .WithMessage("EndTime is required unless Status is Running.");
        RuleFor(x => x.EndTime!.Value).GreaterThanOrEqualTo(x => x.StartTime)
            .When(x => x.EndTime is not null && x.Status != JobRunStatus.Running)
            .WithName("EndTime")
            .WithMessage("EndTime must be on or after StartTime.");
        RuleFor(x => x.Metrics)
            .Must(m => m is null || m.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined)
            .WithMessage("Metrics must be a JSON object.");
        RuleFor(x => x.ErrorMessage).MaximumLength(4000);
    }
}
