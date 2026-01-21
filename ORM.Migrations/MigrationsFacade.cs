using ORM.Core.Mapping;
using ORM.Migrations.Core;

namespace ORM.Migrations;

public static class MigrationsFacade
{
    
    public static (string MigrationId, string UpSqlPath, string DownSqlPath) AddMigrationFromAssembly(
        string consumerAssemblyPath,
        string? @namespace,
        string migrationName,
        string snapshotPath,
        string migrationsDir)
    {
        var entityTypes = Core.EntityDiscovery.DiscoverEntities(consumerAssemblyPath, @namespace);

        var model = new ModelBuilder(useLazyLoading: false).BuildFrom(entityTypes); // model from consumer [file:24]
        var newSnap = Core.SnapshotBuilder.FromOrmModel(model);

        var oldSnap = Core.SnapshotStore.Load(snapshotPath);

        Directory.CreateDirectory(migrationsDir);

        var id = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{migrationName}";
        var upPath = Path.Combine(migrationsDir, $"{id}.up.sql");
        var downPath = Path.Combine(migrationsDir, $"{id}.down.sql");

        if (oldSnap is null)
        {
            var up = string.Join("\n", SqlGen.WrapInTransaction(SqlGen.CreateTableStatementsForWholeSchema(newSnap)));
            var down = string.Join("\n", SqlGen.WrapInTransaction(new List<string> { "-- drop schema (generated)" }
                .Concat(newSnap.Tables.OrderByDescending(t => t.Name).Select(t => SqlGen.DropTableStatement(t.Name))).ToList()));

            File.WriteAllText(upPath, up);
            File.WriteAllText(downPath, down);
        }
        else
        {
            var plan = DiffEngine.Diff(oldSnap, newSnap);
            File.WriteAllText(upPath, string.Join("\n", plan.UpSqlStatements));
            File.WriteAllText(downPath, string.Join("\n", plan.DownSqlStatements));
        }

        Core.SnapshotStore.Save(snapshotPath, newSnap);

        return (id, upPath, downPath);
    }
}