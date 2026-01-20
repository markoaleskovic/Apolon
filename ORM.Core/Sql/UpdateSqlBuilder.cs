using Npgsql;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Sql;

public static class UpdateSqlBuilder
{
    public static (string Sql, NpgsqlParameter[] Params) Build(EntityMap map, object entity)
    {
        var pk = map.PrimaryKey;
        var pkValue = pk.Property.GetValue(entity) ?? throw new InvalidOperationException("PK value is null.");

        // Update all non-PK columns
        var setCols = map.Columns.Where(c => !c.IsPrimaryKey).ToList();
        if (setCols.Count == 0)
            throw new InvalidOperationException($"Entity {map.ClrType.Name} has no updatable columns.");

        var assignments = string.Join(", ", setCols.Select((c, i) => $"{Quote(c.ColumnName)} = @p{i}"));
        var sql = $"UPDATE {Quote(map.TableName)} SET {assignments} WHERE {Quote(pk.ColumnName)} = @pk;";

        var parameters = new List<NpgsqlParameter>();
        for (var i = 0; i < setCols.Count; i++)
        {
            var val = setCols[i].Property.GetValue(entity) ?? DBNull.Value;
            parameters.Add(new NpgsqlParameter($"p{i}", val));
        }
        parameters.Add(new NpgsqlParameter("pk", pkValue));

        return (sql, parameters.ToArray());
    }

    private static string Quote(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
}