namespace ORM.Migrations.Core;

public sealed record MigrationFile(string Id, string UpSql, string DownSql);

public static class MigrationFiles
{
    public static List<MigrationFile> Load(string migrationsDir)
    {
        Directory.CreateDirectory(migrationsDir);

        var ups = Directory.GetFiles(migrationsDir, "*.up.sql")
            .Select(p => new { Path = p, Id = Path.GetFileName(p).Replace(".up.sql", "") })
            .ToDictionary(x => x.Id, x => x.Path);

        var downs = Directory.GetFiles(migrationsDir, "*.down.sql")
            .Select(p => new { Path = p, Id = Path.GetFileName(p).Replace(".down.sql", "") })
            .ToDictionary(x => x.Id, x => x.Path);

        var ids = ups.Keys.Intersect(downs.Keys).OrderBy(x => x).ToList();

        return ids.Select(id => new MigrationFile(
            id,
            File.ReadAllText(ups[id]),
            File.ReadAllText(downs[id])
        )).ToList();
    }
}