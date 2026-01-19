using System.Text;
using Npgsql;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Sql;

public static class InsertSqlBuilder
{
    public static (string Sql, NpgsqlParameter[] Params, ColumnMap? ReturningPk) Build(EntityMap map, object entity)
    {
        // inserting all non identity columns
        var insertCols = map.Columns.Where(c => !(c.IsPrimaryKey && c.IsIdentity)).ToList();
        
        if (insertCols.Count == 0)
            throw new InvalidOperationException($"Entity {map.ClrType.Name} has no insertable columns");
        
        var sql = new StringBuilder();
        sql.Append("INSERT INTO ").Append(Quote(map.TableName)).Append(" (");
        sql.Append(string.Join(", ", insertCols.Select(c => Quote(c.ColumnName))));
        sql.Append(") VALUES (");
        sql.Append(string.Join(", ", insertCols.Select((c, i) => $"@p{i}")));
        sql.Append(")");
        
        ColumnMap? returningPk = null;
        if (map.PrimaryKey.IsIdentity)
        {
            returningPk = map.PrimaryKey;
            sql.Append(" RETURNING ").Append(Quote(map.PrimaryKey.ColumnName));
        }

        var parameters = insertCols
            .Select((c, i) =>
            {
                var value = c.Property.GetValue(entity) ?? DBNull.Value;
                return new NpgsqlParameter($"p{i}", value);
            })
            .ToArray();

        return (sql.ToString(), parameters, returningPk);
        
    }
    private static string Quote(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
}