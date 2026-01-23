using ORM.Core.Mapping.Model;

namespace ORM.Migrations.Core;

public static class SnapshotBuilder
{
    public static SchemaSnapshot FromOrmModel(OrmModel model)
    {
        var snap = new SchemaSnapshot();

        foreach (var map in model.GetEntities())
        {
            var table = new TableSnapshot
            {
                Name = map.TableName,
                PrimaryKeyColumn = map.PrimaryKey.ColumnName
            };

            //columns + uniques
            foreach (var c in map.Columns)
            {
                var clr = Nullable.GetUnderlyingType(c.Property.PropertyType) ?? c.Property.PropertyType;

                table.Columns.Add(new ColumnSnapshot
                {
                    Name = c.ColumnName,
                    ClrType = NormalizeClrType(clr),
                    IsNullable = !c.IsRequired,
                    IsIdentity = c.IsIdentity,
                    DefaultValue = NormalizeDefault(c.DefaultValue),
                });

                if (c.IsUnique && !c.IsPrimaryKey)
                {
                    table.Uniques.Add(new UniqueSnapshot
                    {
                        Table = map.TableName,
                        Column = c.ColumnName,
                        Name = $"ux_{map.TableName}_{c.ColumnName}"
                    });
                }
            }

            //foreign keys:
            foreach (var rel in map.Relationships.Where(r => r.Kind == RelationshipKind.ManyToOne))
            {
                table.ForeignKeys.Add(new ForeignKeySnapshot
                {
                    DependentTable = rel.Dependent.TableName,
                    DependentColumn = rel.ForeignKeyColumn.ColumnName,
                    PrincipalTable = rel.Principal.TableName,
                    PrincipalColumn = rel.Principal.PrimaryKey.ColumnName,
                    Name = $"fk_{rel.Dependent.TableName}_{rel.ForeignKeyColumn.ColumnName}_{rel.Principal.TableName}"
                });
            }

            //deterministic ordering inside table
            table.Columns = table.Columns.OrderBy(c => c.Name).ToList();
            table.Uniques = table.Uniques.OrderBy(u => u.Name).ToList();
            table.ForeignKeys = table.ForeignKeys.OrderBy(f => f.Name).ToList();

            snap.Tables.Add(table);
        }

        //deterministic ordering overall
        snap.Tables = snap.Tables.OrderBy(t => t.Name).ToList();
        return snap;
    }
    
    //helper
    private static string NormalizeClrType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;

        // store stable names only (no assembly/version)
        // in case it stores for some reason version
        if (t.IsEnum) return "enum:" + (t.FullName ?? t.Name);

        return t.FullName ?? t.Name;   //"System.String", "System.Int64"
    }
    private static object? NormalizeDefault(object? v)
    {
        if (v is null) return null;

        return v switch
        {
            DateTime dt => dt,
            decimal d => d,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            bool b => b,
            string s => s,
            _ => v.ToString() //fallback stable string
        };
    }

    
    
}
