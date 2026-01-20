using System.Data;
using ORM.Core.LazyLoading;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Materialization;

public static class EntityMaterializer
{
    public static object Materialize(IDataRecord record, EntityMap map, ILazyLoader? lazyLoader = null)
    {
        var entity = Activator.CreateInstance(map.ClrType) ??
                     throw new InvalidOperationException($"Cannot create instance of type {map.ClrType.Name}");

        foreach (var col in map.Columns)
        {
            var ordinal = record.GetOrdinal(col.ColumnName);
            object? value = record.IsDBNull(ordinal) ? null : record.GetValue(ordinal);

            if (value is not null)
            {
                var targetType = Nullable.GetUnderlyingType(col.Property.PropertyType) ?? col.Property.PropertyType;
                value = Convert.ChangeType(value, targetType);
            }

            col.Property.SetValue(entity, value);
        }

        if (lazyLoader is not null && entity is IHasLazyLoader has)
            has.SetLazyLoader(lazyLoader);

        return entity;
    }

    public static T Materialize<T>(IDataRecord record, EntityMap map, ILazyLoader? lazyLoader = null)
        => (T)Materialize(record, map, lazyLoader);
}