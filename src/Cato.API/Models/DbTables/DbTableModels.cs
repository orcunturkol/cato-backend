using MediatR;

namespace Cato.API.Models.DbTables;

/// <summary>Every table with its live row estimate and data lineage.</summary>
public record GetDbTablesQuery : IRequest<DbTablesDto>;

/// <summary><c>LiveCounts</c> is false when the database gives no live estimates (not Postgres).</summary>
public record DbTablesDto(DateTime GeneratedAt, bool LiveCounts, List<DbTableSummaryDto> Tables);

/// <summary>
/// <c>ApproxRows</c> is the Postgres live-row estimate (null when unknown). <c>Collection</c> is
/// Automatic, Manual, Internal, No writer or Undocumented. <c>Sites</c> are the distinct sites
/// of <c>Sources</c>, for badges and filters. <c>Validation</c> lists the model's unique keys,
/// then the catalog's checks.
/// </summary>
public record DbTableSummaryDto(
    string Table,
    string Entity,
    int ColumnCount,
    long? ApproxRows,
    string Collection,
    string Summary,
    List<string> Sites,
    List<DbTableSourceDto> Sources,
    List<string> Validation,
    List<string> Fallback,
    bool Documented);

public record DbTableSourceDto(
    string Site,
    string Detail,
    string? Url,
    string Via,
    string? JobName,
    string? Producer);

/// <summary>Live row estimates only — cheap enough to poll.</summary>
public record GetDbTableCountsQuery : IRequest<DbTableCountsDto>;

public record DbTableCountsDto(DateTime GeneratedAt, bool LiveCounts, Dictionary<string, long> Rows);

/// <summary>One table with its columns and an exact row count. Null for an unknown table.</summary>
public record GetDbTableQuery(string Table) : IRequest<DbTableDetailDto?>;

/// <summary>The exact count is cached for a few minutes; <c>ExactCountedAt</c> is when it ran.</summary>
public record DbTableDetailDto(
    DateTime GeneratedAt,
    DbTableSummaryDto Summary,
    long ExactRows,
    DateTime ExactCountedAt,
    List<DbColumnDto> Columns);

/// <summary><c>Source</c> is where the column's value comes from: the table's sources unless the column differs.</summary>
public record DbColumnDto(
    string Column,
    string Property,
    string? DataType,
    bool Nullable,
    int? MaxLength,
    bool PrimaryKey,
    string ClrType,
    string Source,
    string? Validation,
    string? Fallback);

/// <summary>
/// All tables as CSV in the column layout of the "DB-Tables&amp;Content" sheet, with exact
/// counts, so the sheet can be refreshed by pasting it over.
/// </summary>
public record ExportDbTablesCsvQuery : IRequest<string>;
