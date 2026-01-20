using Npgsql;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Sql;

public static class DeleteSqlBuilder
{
    public static (string Sql, NpgsqlParameter[] Params) Build(EntityMap map, object entity)
    {
        var pk = map.PrimaryKey;
        var pkValue = pk.Property.GetValue(entity) ?? throw new InvalidOperationException("PK value is null.");

        var sql = $"DELETE FROM {Quote(map.TableName)} WHERE {Quote(pk.ColumnName)} = @p0;";
        return (sql, new[] { new NpgsqlParameter("p0", pkValue) });
    }

    private static string Quote(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
}