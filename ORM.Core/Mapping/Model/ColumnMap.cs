using System.Reflection;

namespace ORM.Core.Mapping.Model;

public sealed record ColumnMap(
    string ColumnName,
    PropertyInfo Property,
    bool IsPrimaryKey,
    bool IsIdentity,
    bool IsRequired,
    bool IsUnique,
    object? DefaultValue);