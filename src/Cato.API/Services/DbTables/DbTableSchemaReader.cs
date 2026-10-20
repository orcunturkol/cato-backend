using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Cato.API.Services.DbTables;

public sealed record DbColumnSchema(
    string Column,
    string Property,
    string? DataType,
    bool Nullable,
    int? MaxLength,
    bool PrimaryKey,
    string ClrType,
    string? ForeignTable);

public sealed record DbTableSchema(
    string Table,
    string Entity,
    IReadOnlyList<DbColumnSchema> Columns,
    IReadOnlyList<IReadOnlyList<string>> UniqueKeys);

/// <summary>
/// Tables and columns as the EF model maps them, so the list never goes stale when a
/// migration adds a table. Order is the model's: entity CLR name, then the key first and
/// the other properties by name — the order of the "DB-Tables&amp;Content" sheet.
/// </summary>
public static class DbTableSchemaReader
{
    /// <param name="model">The design-time model: the runtime model drops store annotations.</param>
    public static IReadOnlyList<DbTableSchema> Read(IModel model) =>
        model.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.GetTableName() != null)
            .Select(ReadTable)
            .ToList();

    private static DbTableSchema ReadTable(IEntityType entity)
    {
        var table = entity.GetTableName()!;
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());

        var columns = entity.GetProperties()
            .Select(p => new DbColumnSchema(
                p.GetColumnName(store) ?? p.Name,
                p.Name,
                p.GetColumnType(store),
                p.IsColumnNullable(store),
                p.GetMaxLength(),
                p.IsPrimaryKey(),
                ClrName(p.ClrType),
                p.GetContainingForeignKeys().Select(fk => fk.PrincipalEntityType.GetTableName()).FirstOrDefault()))
            .ToList();

        var uniqueKeys = entity.GetIndexes()
            .Where(i => i.IsUnique)
            .Select(i => (IReadOnlyList<string>)i.Properties.Select(p => p.GetColumnName(store) ?? p.Name).ToList())
            .ToList();

        return new DbTableSchema(table, entity.ClrType.Name, columns, uniqueKeys);
    }

    /// <summary>C# spelling of a CLR type: <c>int?</c>, <c>string[]</c>, <c>DateOnly</c>.</summary>
    public static string ClrName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return ClrName(inner) + "?";
        if (type.IsArray)
            return ClrName(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`')];
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(ClrName))}>";
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.String => "string",
            TypeCode.Int32 => "int",
            TypeCode.Int64 => "long",
            TypeCode.Int16 => "short",
            TypeCode.Byte => "byte",
            TypeCode.Boolean => "bool",
            TypeCode.Decimal => "decimal",
            TypeCode.Double => "double",
            TypeCode.Single => "float",
            _ => type.Name,
        };
    }
}
