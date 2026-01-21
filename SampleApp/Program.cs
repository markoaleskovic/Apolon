using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ORM.Core;
using ORM.Core.Querying;
using SampleApp.Models;
using ORM.Migrations;
using ORM.Migrations.Core;
using System.Reflection;
using System.Threading.Tasks;


var connStr =
    Environment.GetEnvironmentVariable("ORM_CONN") ??
    "Host=localhost;Port=5432;Database=orm_db;Username=postgres;Password=password";

await using var ctx = new DbContext(
    connStr,
    typeof(Patient),
    typeof(MedicalRecord),
    typeof(Checkup),
    typeof(Medication),
    typeof(Prescription)
);

await Repl.RunAsync(ctx);

static class Repl
{
    public static async Task RunAsync(DbContext ctx)
    {
        PrintBanner();

        while (true)
        {
            Console.Write("orm> ");
            var line = Console.ReadLine();
            if (line is null) return;

            line = line.Trim();
            if (line.Length == 0) continue;

            var args = SplitArgs(line);
            var cmd = args.Count > 0 ? args[0].ToLowerInvariant() : "";

            try
            {
                switch (cmd)
                {
                    case "exit":
                    case "quit":
                        return;

                    case "help":
                        PrintHelp();
                        break;

                    case "conn":
                        await CmdConnAsync(ctx);
                        break;

                    case "schema":
                        await CmdSchemaAsync(ctx, args);
                        break;

                    case "seed":
                        await CmdSeedAsync(ctx);
                        break;

                    case "save":
                        await CmdSaveAsync(ctx);
                        break;

                    case "tracker":
                        CmdTracker(ctx);
                        break;

                    case "patients":
                        await CmdPatientsAsync(ctx, args);
                        break;

                    case "patient":
                        await CmdPatientAsync(ctx, args);
                        break;

                    case "records":
                        await CmdRecordsAsync(ctx, args);
                        break;

                    case "checkups":
                        await CmdCheckupsAsync(ctx, args);
                        break;

                    case "meds":
                        await CmdMedsAsync(ctx, args);
                        break;

                    case "rx":
                        await CmdRxAsync(ctx, args);
                        break;

                    case "lazy":
                        await CmdLazyAsync(ctx, args);
                        break;

                    case "migrations":
                        await CmdMigrationsAsync(ctx, args);
                        break;
                    
                    default:
                        Console.WriteLine("Unknown command. Type 'help'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }
    }

    private static void PrintBanner()
    {
        Console.WriteLine("Custom ORM SampleApp REPL");
        Console.WriteLine("Type 'help' for commands.");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
Core / utility:
  help
  conn                              - open connection + show server version
  schema list                       - list known entity->table mapping (from ModelBuilder)
  schema ping                       - run small SELECTs to verify tables exist
  tracker                           - show tracked entries and states
  save                              - SaveChangesAsync() commit tracked changes
  seed                              - insert small dataset (patients, record, meds, checkup, rx)

Patients:
  patients list [take N]            - list patients (OrderBy LastName)
  patient get <id>                  - FindAsync by PK
  patient add                       - interactive add (staged, then 'save')
  patient update <id>               - interactive update (tracks Modified)
  patient delete <id>               - stage deletion

Medical records (1-1):
  records get-by-patient <patientId>
  records upsert <patientId>        - create if missing, else update

Checkups (1-many):
  checkups list <patientId> [take N]
  checkups add <patientId>
  checkups update <checkupId>
  checkups delete <checkupId>

Medications:
  meds list [take N]
  meds add
  meds update <medId>
  meds delete <medId>

Prescriptions:
  rx list-by-checkup <checkupId>
  rx add <checkupId> <medId>
  rx update <rxId>
  rx delete <rxId>
  
Migrations:
  migrations add <name>              - generate new migration (auto diff vs snapshot)
  migrations list                    - list local migration files
  migrations apply                   - apply pending migrations to DB (tracked)
  migrations rollback [steps N]      - rollback last N migrations (default 1)
  migrations status                  - show DB applied migrations (__orm_migrations)


Lazy-loading demos:
  lazy patient <id>                 - access Patient.MedicalRecord + Patient.Checkups (triggers lazy loads)
  lazy checkup <id>                 - access Checkup.Patient + Checkup.Prescriptions (triggers lazy loads)

Notes:
- Filters supported by your translator are binary comparisons (==, !=, >, >=, <, <=) and && / ||.
- No StartsWith/Contains until you extend PredicateSqlVisitor. 
""");
    }

    // ---------- Commands ----------

    private static async Task CmdConnAsync(DbContext ctx)
    {
        var conn = await ctx.GetOpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select version();";
        var v = await cmd.ExecuteScalarAsync();
        Console.WriteLine($"Connected: {conn.State}");
        Console.WriteLine(v?.ToString());
    }

    private static async Task CmdSchemaAsync(DbContext ctx, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: schema list | schema ping | schema tables | schema columns <table>");
        return;
    }

    var sub = args[1].ToLowerInvariant();

    if (sub == "list")
    {
        Console.WriteLine("This output is hard coded");
        Console.WriteLine("Entities registered in DbContext ctor:");
        Console.WriteLine("  Patient -> patients");
        Console.WriteLine("  MedicalRecord -> medical_records");
        Console.WriteLine("  Checkup -> checkups");
        Console.WriteLine("  Medication -> medications");
        Console.WriteLine("  Prescription -> prescriptions");
        Console.WriteLine("Relationships via [ForeignKey]/[InverseProperty] are discovered by ModelBuilder.");
        return;
    }

    if (sub == "ping")
    {
        var conn = await ctx.GetOpenConnectionAsync();
        string[] tables = { "patients", "medical_records", "checkups", "medications", "prescriptions" };

        foreach (var t in tables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"select count(*) from \"{t}\";";
            var c = await cmd.ExecuteScalarAsync();
            Console.WriteLine($"{t}: count={c}");
        }
        return;
    }

    if (sub == "tables")
    {
        var conn = await ctx.GetOpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            Console.WriteLine(r.GetString(0));

        return;
    }

    if (sub == "columns")
    {
        if (args.Count < 3)
        {
            Console.WriteLine("Usage: schema columns <table>");
            return;
        }

        var table = args[2];

        var conn = await ctx.GetOpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                column_name,
                data_type,
                is_nullable,
                column_default
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @t
            ORDER BY ordinal_position;
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "t";
        p.Value = table;
        cmd.Parameters.Add(p);

        await using var r = await cmd.ExecuteReaderAsync();

        var any = false;
        while (await r.ReadAsync())
        {
            any = true;
            var name = r.GetString(0);
            var type = r.GetString(1);
            var nullable = r.GetString(2);
            var def = r.IsDBNull(3) ? "NULL" : r.GetValue(3)?.ToString();
            Console.WriteLine($"{name,-24} {type,-18} nullable={nullable,-3} default={def}");
        }

        if (!any)
            Console.WriteLine($"No columns found. Check table name '{table}'.");

        return;
    }

    Console.WriteLine("Usage: schema list | schema ping | schema tables | schema columns <table>");
}


    private static async Task CmdMigrationsAsync(DbContext ctx, List<string> args)
{
    if (args.Count < 2)
    {
        Console.WriteLine("Usage: migrations add|list|apply|rollback|status ...");
        return;
    }

    // Paths relative to SampleApp output folder (bin/Debug/netX)
    var baseDir = AppContext.BaseDirectory;
    var migrationsDir = Path.Combine(baseDir, "Migrations");
    var snapshotsDir = Path.Combine(baseDir, "Snapshots");
    var snapshotPath = Path.Combine(snapshotsDir, "last.snapshot.json");

    Directory.CreateDirectory(migrationsDir);
    Directory.CreateDirectory(snapshotsDir);

    var sub = args[1].ToLowerInvariant();

    switch (sub)
    {
        case "add":
        {
            if (args.Count < 3)
            {
                Console.WriteLine("Usage: migrations add <name>");
                return;
            }

            var name = args[2];

            // Use the consumer assembly (SampleApp) and namespace filter
            var consumerAssemblyPath = Assembly.GetExecutingAssembly().Location;
            var ns = "SampleApp.Models";

            // Calls your migrations generator (snapshot + diff + sql files)
            var res = MigrationsFacade.AddMigrationFromAssembly(
                consumerAssemblyPath: consumerAssemblyPath,
                @namespace: ns,
                migrationName: name,
                snapshotPath: snapshotPath,
                migrationsDir: migrationsDir
            );

            Console.WriteLine($"Created migration: {res.MigrationId}");
            Console.WriteLine($"Up:   {res.UpSqlPath}");
            Console.WriteLine($"Down: {res.DownSqlPath}");
            Console.WriteLine($"Snapshot: {snapshotPath}");
            break;
        }

        case "list":
        {
            var files = MigrationFiles.Load(migrationsDir);
            if (files.Count == 0)
            {
                Console.WriteLine("(no local migrations)");
                return;
            }

            foreach (var m in files)
                Console.WriteLine($"{m.Id}");
            break;
        }

        case "apply":
        {
            // Use the same connection string your DbContext uses (best to keep env var as single source)
            var connStr =
                Environment.GetEnvironmentVariable("ORM_CONN") ??
                "Host=localhost;Port=5432;Database=orm_db;Username=postgres;Password=password";

            await new Migrator().ApplyAsync(connStr, migrationsDir);
            Console.WriteLine("Applied pending migrations.");
            break;
        }

        case "rollback":
        {
            var steps = 1;
            var idx = args.FindIndex(a => a.Equals("steps", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < args.Count && int.TryParse(args[idx + 1], out var s))
                steps = s;

            var connStr =
                Environment.GetEnvironmentVariable("ORM_CONN") ??
                "Host=localhost;Port=5432;Database=orm_db;Username=postgres;Password=password";

            await new Migrator().RollbackAsync(connStr, migrationsDir, steps);
            Console.WriteLine($"Rolled back {steps} migration(s).");
            break;
        }

        case "status":
        {
            var connStr =
                Environment.GetEnvironmentVariable("ORM_CONN") ??
                "Host=localhost;Port=5432;Database=orm_db;Username=postgres;Password=password";

            await using var conn = new Npgsql.NpgsqlConnection(connStr);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var repo = new MigrationHistoryRepository();
            await repo.EnsureAsync(conn, null);
            var applied = await repo.GetAppliedAsync(conn, null);

            await tx.CommitAsync();

            if (applied.Count == 0)
            {
                Console.WriteLine("(no applied migrations in DB)");
                return;
            }

            Console.WriteLine("Applied migrations:");
            foreach (var id in applied)
                Console.WriteLine($"  {id}");
            break;
        }

        default:
            Console.WriteLine("Usage: migrations add|list|apply|rollback|status ...");
            break;
    }
}

    
    private static async Task CmdSeedAsync(DbContext ctx)
    {
        // Patient
        var p = new Patient
        {
            FirstName = "Ivana",
            LastName = "Marić",
            Oib = "OIB-" + DateTime.UtcNow.Ticks,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Set<Patient>().Add(p);

        // Meds
        var m1 = new Medication { Name = "Paracetamol-" + DateTime.UtcNow.Ticks, AtcCode = "N02BE01", DefaultDosage = "500mg" };
        var m2 = new Medication { Name = "Ibuprofen-" + (DateTime.UtcNow.Ticks + 1), AtcCode = "M01AE01", DefaultDosage = "200mg" };
        ctx.Set<Medication>().Add(m1);
        ctx.Set<Medication>().Add(m2);

        await ctx.SaveChangesAsync(); // shows identity PK roundtrip

        // Record (1-1)
        var r = new MedicalRecord { PatientId = p.Id, Notes = "Seed record" };
        ctx.Set<MedicalRecord>().Add(r);

        // Checkup
        var c = new Checkup
        {
            PatientId = p.Id,
            CheckupType = "GP",
            PerformedAt = DateTime.UtcNow,
            Price = 25.00m,
            BodyTempC = 36.7f
        };
        ctx.Set<Checkup>().Add(c);

        await ctx.SaveChangesAsync();

        // Prescription (many-to-many with payload)
        var rx = new Prescription
        {
            CheckupId = c.Id,
            MedicationId = m1.Id,
            Dosage = "1x daily",
            StartDate = DateTime.UtcNow.Date,
            EndDate = null
        };
        ctx.Set<Prescription>().Add(rx);

        await ctx.SaveChangesAsync();

        Console.WriteLine($"Seeded PatientId={p.Id}, RecordId={r.Id}, CheckupId={c.Id}, MedId={m1.Id}, RxId={rx.Id}");
    }

    private static async Task CmdSaveAsync(DbContext ctx)
    {
        var affected = await ctx.SaveChangesAsync(); // tx + change tracker [file:13]
        Console.WriteLine($"Saved. Affected rows: {affected}");
    }

    private static void CmdTracker(DbContext ctx)
    {
        var entries = ctx.ChangeTracker.Entries().ToList();
        if (entries.Count == 0)
        {
            Console.WriteLine("No tracked entries.");
            return;
        }

        foreach (var e in entries)
        {
            Console.WriteLine($"{e.Map.ClrType.Name} | State={e.State}");
        }
    }

    private static async Task CmdPatientsAsync(DbContext ctx, List<string> args)
    {
        // patients list [take N]
        if (args.Count < 2 || args[1].ToLowerInvariant() != "list")
        {
            Console.WriteLine("Usage: patients list [take N]");
            return;
        }

        var take = TryReadInt(args, "take") ?? 50;

        var q = ctx.Set<Patient>().OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Take(take);
        var list = await q.ToListAsync();

        foreach (var p in list)
            Console.WriteLine($"{p.Id}: {p.FirstName} {p.LastName} ({p.Oib})");
    }

    private static async Task CmdPatientAsync(DbContext ctx, List<string> args)
    {
        if (args.Count < 2)
        {
            Console.WriteLine("Usage: patient get|add|update|delete ...");
            return;
        }

        var sub = args[1].ToLowerInvariant();
        switch (sub)
        {
            case "get":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: patient get <id>"); return; }
                var id = long.Parse(args[2]);
                var p = await ctx.FindAsync<Patient>(id); // FindAsync exists [file:13]
                if (p is null) Console.WriteLine("Not found.");
                else Console.WriteLine($"{p.Id}: {p.FirstName} {p.LastName} ({p.Oib})");
                break;
            }
            case "add":
            {
                var p = new Patient
                {
                    FirstName = ReadNonEmpty("First name"),
                    LastName = ReadNonEmpty("Last name"),
                    Oib = ReadNonEmpty("OIB (unique)"),
                    CreatedAt = DateTime.UtcNow
                };
                ctx.Set<Patient>().Add(p);
                Console.WriteLine("Staged. Run 'save' to commit.");
                break;
            }
            case "update":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: patient update <id>"); return; }
                var id = long.Parse(args[2]);
                var p = await ctx.FindAsync<Patient>(id);
                if (p is null) { Console.WriteLine("Not found."); return; }

                Console.Write($"First name ({p.FirstName}): ");
                var fn = Console.ReadLine()?.Trim();
                Console.Write($"Last name ({p.LastName}): ");
                var ln = Console.ReadLine()?.Trim();

                if (!string.IsNullOrWhiteSpace(fn)) p.FirstName = fn!;
                if (!string.IsNullOrWhiteSpace(ln)) p.LastName = ln!;

                ctx.Set<Patient>().Update(p);
                Console.WriteLine("Staged Modified. Run 'save'.");
                break;
            }
            case "delete":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: patient delete <id>"); return; }
                var id = long.Parse(args[2]);
                var p = await ctx.FindAsync<Patient>(id);
                if (p is null) { Console.WriteLine("Not found."); return; }
                ctx.Set<Patient>().Remove(p);
                Console.WriteLine("Staged Deleted. Run 'save'.");
                break;
            }
            default:
                Console.WriteLine("Usage: patient get|add|update|delete ...");
                break;
        }
    }

    private static async Task CmdRecordsAsync(DbContext ctx, List<string> args)
    {
        if (args.Count < 2)
        {
            Console.WriteLine("Usage: records get-by-patient <patientId> | records upsert <patientId>");
            return;
        }

        var sub = args[1].ToLowerInvariant();
        if (sub == "get-by-patient")
        {
            if (args.Count < 3) { Console.WriteLine("Usage: records get-by-patient <patientId>"); return; }
            var pid = long.Parse(args[2]);

            var list = await ctx.Set<MedicalRecord>().Where(r => r.PatientId == pid).Take(2).ToListAsync();
            var rec = list.SingleOrDefault();
            Console.WriteLine(rec is null ? "(none)" : $"RecordId={rec.Id} Notes={rec.Notes}");
            return;
        }

        if (sub == "upsert")
        {
            if (args.Count < 3) { Console.WriteLine("Usage: records upsert <patientId>"); return; }
            var pid = long.Parse(args[2]);

            var list = await ctx.Set<MedicalRecord>().Where(r => r.PatientId == pid).Take(2).ToListAsync();
            var rec = list.SingleOrDefault();
            var notes = ReadNonEmpty("Notes");

            if (rec is null)
            {
                rec = new MedicalRecord { PatientId = pid, Notes = notes };
                ctx.Set<MedicalRecord>().Add(rec);
                Console.WriteLine("Record staged Add. Run 'save'.");
            }
            else
            {
                rec.Notes = notes;
                ctx.Set<MedicalRecord>().Update(rec);
                Console.WriteLine("Record staged Update. Run 'save'.");
            }
            return;
        }

        Console.WriteLine("Usage: records get-by-patient <patientId> | records upsert <patientId>");
    }

    private static async Task CmdCheckupsAsync(DbContext ctx, List<string> args)
    {
        if (args.Count < 2) { Console.WriteLine("Usage: checkups list|add|update|delete ..."); return; }
        var sub = args[1].ToLowerInvariant();

        switch (sub)
        {
            case "list":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: checkups list <patientId> [take N]"); return; }
                var pid = long.Parse(args[2]);
                var take = TryReadInt(args, "take") ?? 50;

                var list = await ctx.Set<Checkup>()
                    .Where(c => c.PatientId == pid)
                    .OrderByDescending(c => c.PerformedAt)
                    .Take(take)
                    .ToListAsync();

                foreach (var c in list)
                    Console.WriteLine($"{c.Id}: PatientId={c.PatientId} Type={c.CheckupType} At={c.PerformedAt:u} Price={c.Price} Temp={c.BodyTempC}");
                break;
            }

            case "add":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: checkups add <patientId>"); return; }
                var pid = long.Parse(args[2]);

                var type = ReadCheckupType();
                var price = ReadDecimal("Price");
                var temp = ReadNullableFloat("BodyTempC");

                var c = new Checkup
                {
                    PatientId = pid,
                    CheckupType = type,
                    PerformedAt = DateTime.UtcNow,
                    Price = price,
                    BodyTempC = temp
                };

                ctx.Set<Checkup>().Add(c);
                Console.WriteLine("Checkup staged Add. Run 'save'.");
                break;
            }

            case "update":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: checkups update <checkupId>"); return; }
                var id = long.Parse(args[2]);
                var c = await ctx.FindAsync<Checkup>(id);
                if (c is null) { Console.WriteLine("Not found."); return; }

                Console.WriteLine($"Current: Type={c.CheckupType} Price={c.Price} Temp={c.BodyTempC}");
                Console.Write("Change type? (y/N): ");
                var yn = Console.ReadLine()?.Trim();
                if (string.Equals(yn, "y", StringComparison.OrdinalIgnoreCase))
                    c.CheckupType = ReadCheckupType();

                Console.Write("New price (empty = keep): ");
                var p = Console.ReadLine()?.Trim();
                if (decimal.TryParse(p, out var price)) c.Price = price;

                c.BodyTempC = ReadNullableFloat("BodyTempC");

                ctx.Set<Checkup>().Update(c);
                Console.WriteLine("Checkup staged Update. Run 'save'.");
                break;
            }

            case "delete":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: checkups delete <checkupId>"); return; }
                var id = long.Parse(args[2]);
                var c = await ctx.FindAsync<Checkup>(id);
                if (c is null) { Console.WriteLine("Not found."); return; }
                ctx.Set<Checkup>().Remove(c);
                Console.WriteLine("Checkup staged Delete. Run 'save'.");
                break;
            }

            default:
                Console.WriteLine("Usage: checkups list|add|update|delete ...");
                break;
        }
    }

    private static async Task CmdMedsAsync(DbContext ctx, List<string> args)
    {
        if (args.Count < 2) { Console.WriteLine("Usage: meds list|add|update|delete ..."); return; }
        var sub = args[1].ToLowerInvariant();

        switch (sub)
        {
            case "list":
            {
                var take = TryReadInt(args, "take") ?? 50;
                var list = await ctx.Set<Medication>().OrderBy(m => m.Name).Take(take).ToListAsync();
                foreach (var m in list)
                    Console.WriteLine($"{m.Id}: {m.Name} ATC={m.AtcCode} Default={m.DefaultDosage}");
                break;
            }
            case "add":
            {
                var m = new Medication
                {
                    Name = ReadNonEmpty("Name (unique)"),
                    AtcCode = ReadOptional("ATC code (optional)"),
                    DefaultDosage = ReadNonEmpty("Default dosage")
                };
                ctx.Set<Medication>().Add(m);
                Console.WriteLine("Medication staged Add. Run 'save'.");
                break;
            }
            case "update":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: meds update <medId>"); return; }
                var id = long.Parse(args[2]);
                var m = await ctx.FindAsync<Medication>(id);
                if (m is null) { Console.WriteLine("Not found."); return; }

                Console.Write($"Name ({m.Name}): ");
                var name = Console.ReadLine()?.Trim();
                Console.Write($"ATC ({m.AtcCode}): ");
                var atc = Console.ReadLine()?.Trim();
                Console.Write($"Default dosage ({m.DefaultDosage}): ");
                var dd = Console.ReadLine()?.Trim();

                if (!string.IsNullOrWhiteSpace(name)) m.Name = name!;
                if (!string.IsNullOrWhiteSpace(atc)) m.AtcCode = atc!;
                if (!string.IsNullOrWhiteSpace(dd)) m.DefaultDosage = dd!;

                ctx.Set<Medication>().Update(m);
                Console.WriteLine("Medication staged Update. Run 'save'.");
                break;
            }
            case "delete":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: meds delete <medId>"); return; }
                var id = long.Parse(args[2]);
                var m = await ctx.FindAsync<Medication>(id);
                if (m is null) { Console.WriteLine("Not found."); return; }
                ctx.Set<Medication>().Remove(m);
                Console.WriteLine("Medication staged Delete. Run 'save'.");
                break;
            }
            default:
                Console.WriteLine("Usage: meds list|add|update|delete ...");
                break;
        }
    }

    private static async Task CmdRxAsync(DbContext ctx, List<string> args)
    {
        if (args.Count < 2) { Console.WriteLine("Usage: rx list-by-checkup|add|update|delete ..."); return; }
        var sub = args[1].ToLowerInvariant();

        switch (sub)
        {
            case "list-by-checkup":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: rx list-by-checkup <checkupId>"); return; }
                var cid = long.Parse(args[2]);

                var list = await ctx.Set<Prescription>()
                    .Where(r => r.CheckupId == cid)
                    .OrderBy(r => r.Id)
                    .Take(200)
                    .ToListAsync();

                foreach (var r in list)
                    Console.WriteLine($"{r.Id}: CheckupId={r.CheckupId} MedId={r.MedicationId} Dosage={r.Dosage} Start={r.StartDate:yyyy-MM-dd} End={(r.EndDate.HasValue ? r.EndDate.Value.ToString("yyyy-MM-dd") : "-")}");
                break;
            }

            case "add":
            {
                if (args.Count < 4) { Console.WriteLine("Usage: rx add <checkupId> <medId>"); return; }
                var cid = long.Parse(args[2]);
                var mid = long.Parse(args[3]);

                var r = new Prescription
                {
                    CheckupId = cid,
                    MedicationId = mid,
                    Dosage = ReadNonEmpty("Dosage"),
                    StartDate = DateTime.UtcNow.Date,
                    EndDate = ReadOptionalDate("End date yyyy-mm-dd (optional)")
                };
                ctx.Set<Prescription>().Add(r);
                Console.WriteLine("Prescription staged Add. Run 'save'.");
                break;
            }

            case "update":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: rx update <rxId>"); return; }
                var id = long.Parse(args[2]);
                var r = await ctx.FindAsync<Prescription>(id);
                if (r is null) { Console.WriteLine("Not found."); return; }

                Console.Write($"Dosage ({r.Dosage}): ");
                var d = Console.ReadLine()?.Trim();
                if (!string.IsNullOrWhiteSpace(d)) r.Dosage = d!;

                r.EndDate = ReadOptionalDate("End date yyyy-mm-dd (optional)");

                ctx.Set<Prescription>().Update(r);
                Console.WriteLine("Prescription staged Update. Run 'save'.");
                break;
            }

            case "delete":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: rx delete <rxId>"); return; }
                var id = long.Parse(args[2]);
                var r = await ctx.FindAsync<Prescription>(id);
                if (r is null) { Console.WriteLine("Not found."); return; }
                ctx.Set<Prescription>().Remove(r);
                Console.WriteLine("Prescription staged Delete. Run 'save'.");
                break;
            }

            default:
                Console.WriteLine("Usage: rx list-by-checkup|add|update|delete ...");
                break;
        }
    }

    private static async Task CmdLazyAsync(DbContext ctx, List<string> args)
    {
        if (args.Count < 3)
        {
            Console.WriteLine("Usage: lazy patient <id> | lazy checkup <id>");
            return;
        }

        var target = args[1].ToLowerInvariant();
        var id = long.Parse(args[2]);

        if (target == "patient")
        {
            var p = await ctx.FindAsync<Patient>(id);
            if (p is null) { Console.WriteLine("Not found."); return; }

            Console.WriteLine($"Patient: {p.Id} {p.FirstName} {p.LastName}");
            
            var rec = (await ctx.Set<MedicalRecord>()
                    .Where(r => r.PatientId == p.Id)
                    .Take(2)
                    .ToListAsync())
                .SingleOrDefault();

            Console.WriteLine($"MedicalRecord(query): {(rec is null ? "(null)" : $"Id={rec.Id} Notes='{rec.Notes}'")}");


            var checkups = p.Checkups;
            Console.WriteLine($"Checkups: {checkups.Count}");
            foreach (var c in checkups.Take(5))
                Console.WriteLine($"  - Checkup {c.Id} Type={c.CheckupType} At={c.PerformedAt:u}");
            return;
        }

        if (target == "checkup")
        {
            var c = await ctx.FindAsync<Checkup>(id);
            if (c is null) { Console.WriteLine("Not found."); return; }

            Console.WriteLine($"Checkup: {c.Id} PatientId={c.PatientId} Type={c.CheckupType}");

            var p = c.Patient;
            Console.WriteLine($"Patient(ref): {(p is null ? "(null)" : $"{p.Id} {p.FirstName} {p.LastName}")}");

            var rxs = c.Prescriptions;
            Console.WriteLine($"Prescriptions: {rxs.Count}");
            foreach (var r in rxs.Take(10))
                Console.WriteLine($"  - Rx {r.Id} MedId={r.MedicationId} Dosage={r.Dosage}");
            return;
        }

        Console.WriteLine("Usage: lazy patient <id> | lazy checkup <id>");
    }

    // ---------- Helpers ----------

    private static string ReadNonEmpty(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var s = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(s)) return s!;
        }
    }

    private static string? ReadOptional(string label)
    {
        Console.Write($"{label}: ");
        var s = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static decimal ReadDecimal(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var s = Console.ReadLine()?.Trim();
            if (decimal.TryParse(s, out var v)) return v;
            Console.WriteLine("Invalid decimal.");
        }
    }

    private static float? ReadNullableFloat(string label)
    {
        Console.Write($"{label} (empty = null): ");
        var s = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (float.TryParse(s, out var v)) return v;
        Console.WriteLine("Invalid float, using null.");
        return null;
    }

    private static DateTime? ReadOptionalDate(string label)
    {
        Console.Write($"{label}: ");
        var s = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, out var d)) return d.Date;
        Console.WriteLine("Invalid date. Using null.");
        return null;
    }

    private static string ReadCheckupType()
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GP","BLOOD","X-RAY","CT","MRI","ULTRA","EKG","ECHO","EYE","DERM","DENTA","MAMMO","EEG"
        };

        Console.WriteLine("Type: GP, BLOOD, X-RAY, CT, MRI, ULTRA, EKG, ECHO, EYE, DERM, DENTA, MAMMO, EEG");

        while (true)
        {
            Console.Write("checkup_type> ");
            var s = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(s)) continue;

            // accept both X_RAY and X-RAY from user
            if (string.Equals(s, "X_RAY", StringComparison.OrdinalIgnoreCase)) s = "X-RAY";

            if (allowed.Contains(s))
                return s.ToUpperInvariant(); // store consistently
            Console.WriteLine("Invalid type.");
        }
    }


    private static int? TryReadInt(List<string> args, string key)
    {
        var idx = args.FindIndex(a => a.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= args.Count) return null;
        return int.TryParse(args[idx + 1], out var v) ? v : null;
    }

    private static List<string> SplitArgs(string input)
    {
        // minimal: split by spaces, keep quoted segments
        var res = new List<string>();
        var cur = "";
        var inQuotes = false;

        foreach (var ch in input)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (cur.Length > 0) { res.Add(cur); cur = ""; }
                continue;
            }
            cur += ch;
        }
        if (cur.Length > 0) res.Add(cur);
        return res;
    }
}
