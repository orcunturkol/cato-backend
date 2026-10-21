using System.Text;
using Cato.API.Models.DbTables;
using Cato.API.Services.DbTables;
using Cato.Infrastructure.Database;
using MediatR;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Cato.API.Services.Handlers.DbTables;

/// <summary>
/// One row per column, like the sheet. The key row of each table carries the table's
/// record count, all of its sources, its validation and its fallback; the other rows
/// carry only what is true for that column.
/// </summary>
public class ExportDbTablesCsvHandler : IRequestHandler<ExportDbTablesCsvQuery, string>
{
    public static readonly string[] Header =
        ["Table", "Column", "Data Type", "Nullable", "Max Length", "PK", "CLR Type", "Record Count", "Data Source", "Validation", "Fallback"];

    private readonly CatoDbContext _db;
    private readonly DbTableStats _stats;

    public ExportDbTablesCsvHandler(CatoDbContext db, DbTableStats stats)
    {
        _db = db;
        _stats = stats;
    }

    public async Task<string> Handle(ExportDbTablesCsvQuery request, CancellationToken ct)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Header);

        // Postgres row estimates in one query: exact counts of every table take longer than the
        // 30 s command limit on the server (77 s and a 500 on 2026-10-21). Off Postgres
        // (tests) there are no estimates, so count exactly.
        var estimates = await _stats.EstimateRowsAsync(ct);
        foreach (var schema in DbTableSchemaReader.Read(_db.GetService<IDesignTimeModel>().Model))
        {
            var rows = estimates is not null
                ? estimates.GetValueOrDefault(schema.Table)
                : (await _stats.CountRowsAsync(schema.Table, ct)).Rows;
            var summary = DbTableView.Summary(schema, null);
            var keyRow = true;

            foreach (var column in DbTableView.Columns(schema))
            {
                AppendRow(csv,
                [
                    schema.Table,
                    column.Column,
                    column.DataType ?? "",
                    column.Nullable ? "nullable" : "NOT NULL",
                    column.MaxLength?.ToString() ?? "",
                    column.PrimaryKey ? "PK" : "",
                    column.ClrType,
                    keyRow ? rows.ToString() : "",
                    keyRow ? TableSources(summary) : column.Source,
                    keyRow ? Lines(summary.Validation.Append(column.Validation)) : column.Validation ?? "",
                    keyRow ? Lines(summary.Fallback.Append(column.Fallback)) : column.Fallback ?? "",
                ]);
                keyRow = false;
            }
        }

        return csv.ToString();
    }

    private static string TableSources(DbTableSummaryDto summary) =>
        summary.Sources.Count == 0
            ? summary.Collection
            : Lines(summary.Sources.Select(s => $"{s.Site}: {s.Detail}{(s.Url is null ? "" : $" ({s.Url})")} — {s.Via}"));

    private static string Lines(IEnumerable<string?> lines) =>
        string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));

    private static void AppendRow(StringBuilder csv, IEnumerable<string> fields)
    {
        csv.AppendJoin(',', fields.Select(Escape));
        csv.Append("\r\n");
    }

    private static string Escape(string field) =>
        field.IndexOfAny([',', '"', '\n', '\r']) < 0 ? field : $"\"{field.Replace("\"", "\"\"")}\"";
}
