using ORM.Migrations;

static string GetArg(string[] args, string key)
{
    var i = Array.FindIndex(args, a => a.Equals(key, StringComparison.OrdinalIgnoreCase));
    if (i < 0 || i + 1 >= args.Length) throw new ArgumentException($"Missing {key}");
    return args[i + 1];
}

static string? TryGetArg(string[] args, string key)
{
    var i = Array.FindIndex(args, a => a.Equals(key, StringComparison.OrdinalIgnoreCase));
    return (i < 0 || i + 1 >= args.Length) ? null : args[i + 1];
}

var cmd = args.FirstOrDefault()?.ToLowerInvariant();
if (cmd is null)
{
    Console.WriteLine("Commands:");
    Console.WriteLine("  add --assembly <path> [--ns <namespace>] --name <migrationName> --snap <snapshotPath> --out <migrationsDir>");
    return;
}

if (cmd == "add")
{
    var assemblyPath = GetArg(args, "--assembly");
    var ns = TryGetArg(args, "--ns");
    var name = GetArg(args, "--name");
    var snap = GetArg(args, "--snap");
    var outDir = GetArg(args, "--out");

    var result = MigrationsFacade.AddMigrationFromAssembly(
        consumerAssemblyPath: assemblyPath,
        @namespace: ns,
        migrationName: name,
        snapshotPath: snap,
        migrationsDir: outDir
    );

    Console.WriteLine($"Created migration: {result.MigrationId}");
    Console.WriteLine($"Up:   {result.UpSqlPath}");
    Console.WriteLine($"Down: {result.DownSqlPath}");
    return;
}

Console.WriteLine("Unknown command.");