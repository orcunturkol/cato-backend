using System.Text.Json;
using Cato.API.Models.ScheduledJobs;
using Cato.API.Services.JobRuns;
using Cato.Domain.Entities;

namespace Cato.API.Services.Handlers.ScheduledJobs;

internal static class ScheduledJobMapping
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ScheduledJobDto ToDto(ScheduledJob job, DateTime nowUtc) => new(
        job.Name,
        job.Label,
        job.AgentGroup,
        job.Module,
        job.Args,
        job.Cron,
        job.Enabled,
        job.TimeoutSeconds,
        job.AlertFailedRatio,
        job.AlertMinItems,
        job.ManagedBy,
        Editable: job.ManagedBy == ScheduledJobManagedBy.Dispatcher,
        job.Enabled ? ScheduledJobRules.NextRuns(job.Cron, nowUtc) : [],
        DateTime.SpecifyKind(job.UpdatedAt, DateTimeKind.Utc),
        job.UpdatedBy);

    public static string ToJson(ScheduleSettings settings) => JsonSerializer.Serialize(settings, Json);
}
