using System.Reflection;
using ORM.Core.Mapping.Model;
using ORM.Core.Mapping.Attributes;

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
        //fallbacks to the name itself if [Table("not present")]
        var tableName = tableAttr?.Name ?? type.Name;
        
        //columns from public instance properties
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var columns = new List<ColumnMap>();
        ColumnMap? primarykey = null;

        foreach (var p in props)
        {
            //continue for now
            if (!p.CanRead || !p.CanWrite) continue;
            
            //same fallback as before
            var colAttr = p.GetCustomAttribute<ColumnAttribute>(inherit: false);
            var columnName = colAttr?.Name ?? p.Name;
     
            var isPrimaryKey = p.IsDefined(typeof(KeyAttribute), inherit: false);
            var isIdentity = p.IsDefined(typeof(DatabaseGeneratedIdentityAttribute), inherit: false);

            //property named "Id" is pk if [Key] doesnt exist
            if (!isPrimaryKey && string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                isPrimaryKey = true;

            var colMap = new ColumnMap(columnName, p, isPrimaryKey, isIdentity);
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