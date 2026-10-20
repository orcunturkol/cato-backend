using System.Collections.Concurrent;
using Cato.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Cato.API.Services.DbTables;

/// <summary>Exact counts by table, kept across requests (singleton).</summary>
public sealed class DbTableCountCache
{
    private readonly ConcurrentDictionary<string, DbTableCount> _counts = new(StringComparer.Ordinal);

    public DbTableCount? Get(string table, DateTime now, TimeSpan maxAge) =>
        _counts.TryGetValue(table, out var count) && now - count.CountedAt < maxAge ? count : null;

    public void Set(string table, DbTableCount count) => _counts[table] = count;
}

public sealed record DbTableCount(long Rows, DateTime CountedAt);

/// <summary>
/// Row counts. The list uses Postgres statistics (<c>n_live_tup</c>): it moves with every
/// insert and delete and costs no table scan, so the page can poll it. An exact
/// <c>COUNT(*)</c> scans the table — some hold millions of rows — so it is cached.
/// </summary>
public sealed class DbTableStats
{
    public static readonly TimeSpan ExactCountMaxAge = TimeSpan.FromMinutes(5);

    private readonly CatoDbContext _db;
    private readonly DbTableCountCache _cache;
    private readonly TimeProvider _clock;

    public DbTableStats(CatoDbContext db, DbTableCountCache cache, TimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    /// <summary>Live estimates by table name; null off Postgres (the SQLite tests).</summary>
    public async Task<Dictionary<string, long>?> EstimateRowsAsync(CancellationToken ct)
    {
        if (!_db.Database.IsNpgsql()) return null;

        var rows = await _db.Database
            .SqlQueryRaw<TableEstimate>(
                """SELECT relname AS "Table", n_live_tup AS "Rows" FROM pg_stat_user_tables WHERE schemaname = current_schema()""")
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Table, r => r.Rows, StringComparer.Ordinal);
    }

    /// <summary>
    /// Exact row count, at most <see cref="ExactCountMaxAge"/> old.
    /// <paramref name="table"/> must be a table name from the EF model, never request input.
    /// </summary>
    public async Task<DbTableCount> CountRowsAsync(string table, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        if (_cache.Get(table, now, ExactCountMaxAge) is { } cached) return cached;

        var sql = $"SELECT COUNT(*) AS \"Value\" FROM \"{table.Replace("\"", "\"\"")}\"";
        var rows = await _db.Database.SqlQueryRaw<long>(sql).SingleAsync(ct);

        var count = new DbTableCount(rows, now);
        _cache.Set(table, count);
        return count;
    }

    private sealed class TableEstimate
    {
        public string Table { get; set; } = "";
        public long Rows { get; set; }
    }
}
