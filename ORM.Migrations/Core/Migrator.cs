using Npgsql;

namespace ORM.Migrations.Core;

public sealed class Migrator
{
    private readonly MigrationHistoryRepository _history = new();

    public async Task ApplyAsync(string connStr, string migrationsDir)
    {
        var files = MigrationFiles.Load(migrationsDir);
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await _history.EnsureAsync(conn, (NpgsqlTransaction)tx);
        var applied = await _history.GetAppliedAsync(conn, (NpgsqlTransaction)tx);

        foreach (var m in files.Where(m => !applied.Contains(m.Id)))
        {
            await ExecuteSqlAsync(conn, (NpgsqlTransaction)tx, m.UpSql);
            await _history.InsertAsync(m.Id, conn, (NpgsqlTransaction)tx);
        }

        await tx.CommitAsync();
    }

    public async Task RollbackAsync(string connStr, string migrationsDir, int steps = 1)
    {
        var files = MigrationFiles.Load(migrationsDir).ToDictionary(m => m.Id);
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await _history.EnsureAsync(conn, (NpgsqlTransaction)tx);

        for (var i = 0; i < steps; i++)
        {
            var last = await _history.GetLastAppliedAsync(conn, (NpgsqlTransaction)tx);
            if (last is null) break;
            if (!files.TryGetValue(last, out var mig))
                throw new InvalidOperationException($"Last applied migration '{last}' not found on disk.");

            await ExecuteSqlAsync(conn, (NpgsqlTransaction)tx, mig.DownSql);
            await _history.DeleteAsync(last, conn, (NpgsqlTransaction)tx);
        }

        await tx.CommitAsync();
    }

    private static async Task ExecuteSqlAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync();
    }
}
