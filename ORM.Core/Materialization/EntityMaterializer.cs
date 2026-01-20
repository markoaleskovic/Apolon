using System.Data;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Materialization;

public static class EntityMaterializer
{
    //suppose select always returns id, name (no alias)
    public static object Materialize(IDataRecord record, EntityMap map)
    {
        var entity = Activator.CreateInstance(map.ClrType) ??
                     throw new InvalidOperationException($"Cannot create instance of type {map.ClrType.Name}");


        foreach (var col in map.Columns)
        {
            //index
            var ordinal = record.GetOrdinal(col.ColumnName);
            object? value = record.IsDBNull(ordinal) ? null : record.GetValue(ordinal);

            
            //if nullable type turn to non-nullable (int? => int)
            if (value is not null)
            {
                var targetType = Nullable.GetUnderlyingType(col.Property.PropertyType) ?? col.Property.PropertyType;
                value = Convert.ChangeType(value, targetType);
            }
            col.Property.SetValue(entity, value);
        }

        return entity;
        
    }

    public static T Materialize<T>(IDataRecord record, EntityMap map) where T : class => (T)Materialize(record, map);

}