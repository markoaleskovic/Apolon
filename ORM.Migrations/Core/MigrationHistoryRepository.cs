using Npgsql;

namespace ORM.Migrations.Core;

public sealed class MigrationHistoryRepository
{
    public const string TableName = "__orm_migrations";

    public async Task EnsureAsync(NpgsqlConnection conn, NpgsqlTransaction? tx)
    {
        var sql = $"""
            CREATE TABLE IF NOT EXISTS "{TableName}" (
              id BIGSERIAL PRIMARY KEY,
              migration_id VARCHAR(150) NOT NULL UNIQUE,
              applied_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
""";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetAppliedAsync(NpgsqlConnection conn, NpgsqlTransaction? tx)
    {
        var list = new List<string>();
        var sql = $"""SELECT migration_id FROM "{TableName}" ORDER BY applied_at;""";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    public async Task<string?> GetLastAppliedAsync(NpgsqlConnection conn, NpgsqlTransaction? tx)
    {
        var sql = $"""SELECT migration_id FROM "{TableName}" ORDER BY applied_at DESC LIMIT 1;""";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        var v = await cmd.ExecuteScalarAsync();
        return v as string;
    }

    public async Task InsertAsync(string migrationId, NpgsqlConnection conn, NpgsqlTransaction? tx)
    {
        var sql = $"""INSERT INTO "{TableName}"(migration_id) VALUES (@id);""";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", migrationId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string migrationId, NpgsqlConnection conn, NpgsqlTransaction? tx)
    {
        var sql = $"""DELETE FROM "{TableName}" WHERE migration_id = @id;""";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", migrationId);
        await cmd.ExecuteNonQueryAsync();
    }
    
  
}
