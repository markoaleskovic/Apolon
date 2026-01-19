namespace ORM.Core.Mapping.Model;

public sealed class EntityMap
{
    public required Type ClrType { get; init; }
    public required string TableName { get; init; }
    public required ColumnMap PrimaryKey { get; init; }
    public required IReadOnlyList<ColumnMap> Columns { get; init; }
}