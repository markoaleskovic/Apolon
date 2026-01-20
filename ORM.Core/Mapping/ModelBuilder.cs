using System.Reflection;
using ORM.Core.Mapping.Attributes;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Mapping;

public sealed class ModelBuilder
{
    public OrmModel BuildFrom(params Type[] entityTypes)
    {
        var model = new OrmModel();

        foreach (var type in entityTypes)
        {
            var entityMap = BuildEntityMap(type);
            model.AddEntity(entityMap);
        }

        return model;
    }

    private static EntityMap BuildEntityMap(Type type)
    {
        var tableAttr = type.GetCustomAttribute<TableAttribute>(inherit: false);
        var tableName = tableAttr?.Name ?? type.Name;

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var columns = new List<ColumnMap>();
        ColumnMap? primarykey = null;

        foreach (var p in props)
        {
            if (!p.CanRead || !p.CanWrite) continue;

            var colAttr = p.GetCustomAttribute<ColumnAttribute>(inherit: false);
            var columnName = colAttr?.Name ?? p.Name;

            var isPrimaryKey = p.IsDefined(typeof(KeyAttribute), inherit: false);
            var isIdentity = p.IsDefined(typeof(DatabaseGeneratedIdentityAttribute), inherit: false);

            if (!isPrimaryKey && string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                isPrimaryKey = true;

            var isUnique = p.IsDefined(typeof(UniqueAttribute), inherit: false);

            var defaultAttr = p.GetCustomAttribute<DefaultValueAttribute>(inherit: false);
            object? defaultValue = defaultAttr?.Value;

            // Required:
            // - explicit [Required], OR
            // - non-nullable value type (int, DateTime, etc.)
            var isNonNullableValueType =
                p.PropertyType.IsValueType && Nullable.GetUnderlyingType(p.PropertyType) is null;

            var isRequired =
                p.IsDefined(typeof(RequiredAttribute), inherit: false) || isNonNullableValueType;

            var colMap = new ColumnMap(
                columnName,
                p,
                isPrimaryKey,
                isIdentity,
                isRequired,
                isUnique,
                defaultValue
            );

            columns.Add(colMap);

            if (isPrimaryKey)
                primarykey = colMap;
        }

        if (primarykey is null)
            throw new InvalidOperationException($"Entity {type.Name} has no primary key");

        return new EntityMap
        {
            ClrType = type,
            TableName = tableName,
            Columns = columns,
            PrimaryKey = primarykey
        };
    }
}
