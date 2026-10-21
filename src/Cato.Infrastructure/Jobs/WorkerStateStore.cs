using Cato.Domain.Entities;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cato.Infrastructure.Jobs;

/// <summary>Reads and writes worker_state rows, each in its own scope and save.</summary>
public static class WorkerStateStore
{
    public static async Task<string?> GetAsync(IServiceScopeFactory scopes, string key, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        return await db.WorkerStates.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
    }

    public static async Task SetAsync(IServiceScopeFactory scopes, string key, string value, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatoDbContext>();
        var row = await db.WorkerStates.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
            db.WorkerStates.Add(row = new WorkerState { Key = key });
        row.Value = value;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
