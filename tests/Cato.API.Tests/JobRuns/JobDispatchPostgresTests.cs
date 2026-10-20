using Cato.API.Services.Dispatch;
using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cato.API.Tests.JobRuns;

/// <summary>
/// The dispatcher's queries on real Postgres (the other tests use SQLite). Runs only when
/// CATO_PG_TEST_CONNECTION points at a migrated, disposable database; it writes there.
/// </summary>
public class JobDispatchPostgresTests
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("CATO_PG_TEST_CONNECTION");

    [Fact]
    public async Task Dispatch_claim_finish_and_housekeeping_work_on_postgres()
    {
        if (string.IsNullOrWhiteSpace(Connection)) return;

        var options = new DbContextOptionsBuilder<CatoDbContext>().UseNpgsql(Connection).Options;
        var now = DateTime.UtcNow;
        var name = $"pgtest_{Guid.NewGuid():N}"[..20];
        await using (var setup = new CatoDbContext(options))
        {
            setup.ScheduledJobs.Add(new ScheduledJob
            {
                Name = name, Label = name, AgentGroup = JobAgentGroups.Events, Module = "orchestrators.run_x",
                Cron = "* * * * *", Enabled = true, TimeoutSeconds = 60,
                ManagedBy = ScheduledJobManagedBy.Dispatcher, UpdatedAt = now.AddHours(-1),
            });
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var db = new CatoDbContext(options);
            var service = new JobDispatchService(db, Options.Create(new JobDispatcherSettings()));

            Assert.True(await service.DispatchDueAsync(now, default) >= 1);
            Assert.Equal(0, await service.DispatchDueAsync(now, default));
            var run = await service.ClaimAsync(JobAgentGroups.Events, "events@pgtest", "pg", now, default);
            Assert.Equal(name, run!.JobName);
            Assert.Equal(FinishOutcome.Recorded, await service.FinishAsync(run.DispatchId, "events@pgtest", 0, null, now, default));

            // The next minute's slot, while nothing is active, is pending again.
            await service.DispatchDueAsync(now.AddMinutes(1), default);
            Assert.Equal(1, await db.JobDispatches.CountAsync(d => d.JobName == name && d.Status == JobDispatchStatus.Pending));
            Assert.True(await service.HousekeepAsync(now.AddHours(7), default) >= 1);
            Assert.Equal(0, await db.JobDispatches.CountAsync(d => d.JobName == name && d.Status == JobDispatchStatus.Pending));
        }
        finally
        {
            await using var cleanup = new CatoDbContext(options);
            await cleanup.JobDispatches.Where(d => d.JobName == name).ExecuteDeleteAsync();
            await cleanup.JobAgents.Where(a => a.AgentId == "events@pgtest").ExecuteDeleteAsync();
            await cleanup.ScheduledJobs.Where(j => j.Name == name).ExecuteDeleteAsync();
        }
    }
}
