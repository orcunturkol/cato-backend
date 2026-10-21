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

        foreach (var schema in DbTableSchemaReader.Read(_db.GetService<IDesignTimeModel>().Model))
        {
            var count = await _stats.CountRowsAsync(schema.Table, ct);
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
                    keyRow ? count.Rows.ToString() : "",
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
