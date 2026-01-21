using Npgsql;

namespace ORM.Migrations.Core;

public sealed class Migrator
{
    private readonly MigrationHistoryRepository _history = new();

    public async Task ApplyAsync(string connStr, string migrationsDir)
    {
        var migrations = MigrationFiles.Load(migrationsDir);

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        var repo = new MigrationHistoryRepository();
        await repo.EnsureAsync(conn, tx);

        var applied = await repo.GetAppliedAsync(conn, tx);
        var pending = migrations.Where(m => !applied.Contains(m.Id)).ToList();

        foreach (var m in pending)
        {
            await using (var cmd = new NpgsqlCommand(m.UpSql, conn, tx))
                await cmd.ExecuteNonQueryAsync();

            await repo.InsertAsync(m.Id, conn, tx);
        }

        await tx.CommitAsync();
    }


    public async Task RollbackAsync(string connStr, string migrationsDir, int steps = 1)
    {
        var migrations = MigrationFiles.Load(migrationsDir).ToDictionary(m => m.Id);

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        var repo = new MigrationHistoryRepository();
        await repo.EnsureAsync(conn, tx);

        for (var i = 0; i < steps; i++)
        {
            var last = await repo.GetLastAppliedAsync(conn, tx);
            if (last is null) break;

            if (!migrations.TryGetValue(last, out var mig))
                throw new InvalidOperationException($"Migration '{last}' not found on disk.");

            await using (var cmd = new NpgsqlCommand(mig.DownSql, conn, tx))
                await cmd.ExecuteNonQueryAsync();

            await repo.DeleteAsync(last, conn, tx);
        }

        await tx.CommitAsync();
    }


    private static async Task ExecuteSqlAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync();
    }
}
