using Cato.API.Models.DbTables;

namespace Cato.API.Services.DbTables;

/// <summary>Joins a table's model schema with its catalog entry into the API shapes.</summary>
public static class DbTableView
{
    public const string Undocumented = "Undocumented";

    public static DbTableSummaryDto Summary(DbTableSchema schema, long? approxRows)
    {
        var doc = DbTableCatalog.Find(schema.Table);
        var sources = doc?.Sources
            .Select(s => new DbTableSourceDto(s.Site, s.Detail, s.Url, s.Via, s.JobName, s.Producer))
            .ToList() ?? [];

        var validation = schema.UniqueKeys
            .Select(key => $"Unique key: {string.Join(" + ", key)}.")
            .Concat(doc?.Validation ?? [])
            .ToList();

        return new DbTableSummaryDto(
            schema.Table,
            schema.Entity,
            schema.Columns.Count,
            approxRows,
            doc?.Collection ?? Undocumented,
            doc?.Summary ?? "Not in DbTableCatalog yet.",
            sources.Select(s => s.Site).Distinct().ToList(),
            sources,
            validation,
            doc?.Fallback.ToList() ?? [],
            doc != null);
    }

    public static List<DbColumnDto> Columns(DbTableSchema schema)
    {
        var doc = DbTableCatalog.Find(schema.Table);

        return schema.Columns.Select(c =>
        {
            var note = doc?.Columns?.GetValueOrDefault(c.Property);
            var keys = schema.UniqueKeys
                .Where(key => key.Contains(c.Column))
                .Select(key => $"Unique key: {string.Join(" + ", key)}");
            var validation = string.Join(". ", keys.Append(note?.Validation).OfType<string>());

            return new DbColumnDto(
                c.Column, c.Property, c.DataType, c.Nullable, c.MaxLength, c.PrimaryKey, c.ClrType,
                note?.Source ?? DefaultSource(c, doc),
                validation.Length > 0 ? validation : null,
                note?.Fallback);
        }).ToList();
    }

    /// <summary>Short form of the table's sources, e.g. "Steamworks: Wishlist CSV reports".</summary>
    public static string TableSource(DbTableDoc? doc) =>
        doc is null ? "Not documented"
        : doc.Sources.Count == 0 ? "No writer"
        : string.Join("; ", doc.Sources.Select(s => $"{s.Site}: {s.Detail}").Distinct());

    private static string DefaultSource(DbColumnSchema column, DbTableDoc? doc) => column switch
    {
        { PrimaryKey: true, Property: "Id" } => "CATO: new id on insert",
        { Property: "CreatedAt" } => "CATO: set on insert",
        { Property: "UpdatedAt" } => "CATO: set on every save",
        { ForeignTable: { } table } => $"CATO: link to {table}",
        _ => TableSource(doc),
    };
}
