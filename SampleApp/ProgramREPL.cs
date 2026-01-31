using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using ORM.Core;
using ORM.Core.Mapping.Model;
using ORM.Core.Querying;
using ORM.Migrations;
using ORM.Migrations.Core;
using SampleApp.Models;

var connStr =
    Environment.GetEnvironmentVariable("ORM_CONN") ??
    "Host=localhost;Port=5432;Database=orm_db;Username=postgres;Password=password";

await using var ctx = new DbContext(connStr);
await ctx.ConnectAsync();
ctx.BuildModel();

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
                        await CmdMigrationsAsync(args);
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
  schema tables                     - list tables from information_schema
  schema columns <table>            - list columns for a table
  tracker                           - show tracked entries and states
  save                              - SaveChangesAsync() commit tracked changes
  seed                              - insert small dataset (patients, record, meds, checkup, rx)

Patients:
  patients list [take N]            - list patients (OrderBy LastName)
  patient get <id>                  - FindAsync by PK
  patient add                       - interactive add (dynamic, reads current model columns)
  patient update <id>               - interactive update (dynamic, reads current model columns)
  patient delete <id>               - stage deletion
  patients where <expr> [take N]    - filter patients (AND,OR,comparisons)
  patient find <text> [take N]        - find by FirstName/LastName/Oib/Phone

Medical records (1-1):
  records get-by-patient <patientId>
  records upsert <patientId>        - create if missing, else update

Checkups (1-many):
  checkups list <patientId> [take N]
  checkups add <patientId>
  checkups update <checkupId>
  checkups delete <checkupId>
  checkups where <expr> [take N]      - filter checkups

Medications:
  meds list [take N]
  meds add
  meds update <medId>
  meds delete <medId>
  meds where <expr> [take N]          - filter medications
  meds find <text> [take N]           - find by Name/AtcCode

Prescriptions:
  rx list-by-checkup <checkupId>
  rx add <checkupId> <medId>
  rx update <rxId>
  rx delete <rxId>
  rx where <expr> [take N]            - filter prescriptions

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
            var model = GetModel(ctx);

            Console.WriteLine("Entities registered:");
            foreach (var (clr, table) in model.GetType()
                         .GetField("_entities", BindingFlags.Instance | BindingFlags.NonPublic)!
                         .GetValue(model) is Dictionary<Type, EntityMap> dict
                         ? dict.Values.OrderBy(m => m.TableName).Select(m => (m.ClrType, m.TableName))
                         : Enumerable.Empty<(Type, string)>())
            {
                Console.WriteLine($"  {clr.Name} -> {table}");
            }

            Console.WriteLine("Relationships via [ForeignKey]/[InverseProperty] are discovered by ModelBuilder.");
            return;
        }

        if (sub == "ping")
        {
            var conn = await ctx.GetOpenConnectionAsync();
            var model = GetModel(ctx);

            var tables = GetAllEntityMaps(model).Select(m => m.TableName).Distinct().OrderBy(x => x).ToArray();
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

    private static async Task CmdMigrationsAsync(List<string> args)
    {
        if (args.Count < 2)
        {
            Console.WriteLine("Usage: migrations add|list|apply|rollback|status ...");
            return;
        }

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

                var consumerAssemblyPath = Assembly.GetExecutingAssembly().Location;
                var ns = "SampleApp.Models";

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

                var repo = new MigrationHistoryRepository();
                await repo.EnsureAsync(conn, null);
                var applied = await repo.GetAppliedAsync(conn, null);

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
    var model = GetModel(ctx);
    var ticks = DateTime.UtcNow.Ticks;

    // ---------- Medications (10) ----------
    var meds = new List<Medication>();

    Medication NewMed(string name, string atc, string dosage)
    {
        var m = new Medication();
        TrySetIfExists(m, "Name", $"{name}-{ticks}-{meds.Count + 1}");
        TrySetIfExists(m, "AtcCode", atc);
        TrySetIfExists(m, "DefaultDosage", dosage);
        EnsureRequiredColumnsHaveValues(model.GetEntity(typeof(Medication)), m);
        ctx.Set<Medication>().Add(m);
        meds.Add(m);
        return m;
    }

    NewMed("Paracetamol", "N02BE01", "500mg");
    NewMed("Ibuprofen", "M01AE01", "200mg");
    NewMed("Omeprazole", "A02BC01", "20mg");
    NewMed("Amoxicillin", "J01CA04", "500mg");
    NewMed("Cetirizine", "R06AE07", "10mg");
    NewMed("Metformin", "A10BA02", "500mg");
    NewMed("Amlodipine", "C08CA01", "5mg");
    NewMed("Atorvastatin", "C10AA05", "20mg");
    NewMed("Salbutamol", "R03AC02", "100mcg");
    NewMed("Diclofenac", "M01AB05", "50mg");

    // Save now so Medication.Id exists
    await ctx.SaveChangesAsync();

    // ---------- Patients (8) ----------
    var patients = new List<Patient>();

    Patient NewPatient(string first, string last, string? phone, int oibSuffix)
    {
        var p = new Patient();
        TrySetIfExists(p, "FirstName", first);
        TrySetIfExists(p, "LastName", last);
        TrySetIfExists(p, "Phone", phone);
        TrySetIfExists(p, "Oib", $"OIB-{ticks}-{oibSuffix}");
        TrySetIfExists(p, "CreatedAt", DateTime.UtcNow.AddMinutes(-oibSuffix));
        EnsureRequiredColumnsHaveValues(model.GetEntity(typeof(Patient)), p);
        ctx.Set<Patient>().Add(p);
        patients.Add(p);
        return p;
    }

    NewPatient("Ivana", "Marić", "+385911111111", 1);
    NewPatient("Marko", "Horvat", "+385922222222", 2);
    NewPatient("Ana", "Kovač", "+385933333333", 3);
    NewPatient("Luka", "Babić", null, 4);
    NewPatient("Petra", "Novak", "+385944444444", 5);
    NewPatient("Maja", "Radić", "+385955555555", 6);
    NewPatient("Ivan", "Šarić", null, 7);
    NewPatient("Ema", "Jurić", "+385966666666", 8);

    // Save so Patient.Id exists
    await ctx.SaveChangesAsync();

    // ---------- MedicalRecords (1-1 for each patient) ----------
    var records = new List<MedicalRecord>();

    void AddRecord(Patient p, string notes)
    {
        var r = new MedicalRecord();
        TrySetIfExists(r, "PatientId", p.Id);
        TrySetIfExists(r, "Notes", notes);
        EnsureRequiredColumnsHaveValues(model.GetEntity(typeof(MedicalRecord)), r);
        ctx.Set<MedicalRecord>().Add(r);
        records.Add(r);
    }

    AddRecord(patients[0], "No chronic conditions. Occasional headaches.");
    AddRecord(patients[1], "Penicillin allergy reported.");
    AddRecord(patients[2], "Hypertension suspected; monitor BP.");
    AddRecord(patients[3], "Asthma history; uses inhaler when needed.");
    AddRecord(patients[4], "Seasonal allergies.");
    AddRecord(patients[5], "Type 2 diabetes; lifestyle counseling.");
    AddRecord(patients[6], "High cholesterol; diet advised.");
    AddRecord(patients[7], "GERD symptoms; avoid late meals.");

    // ---------- Checkups (30) ----------
    var checkups = new List<Checkup>();
    var types = new[] { "GP", "BLOOD", "X-RAY", "CT", "MRI", "ULTRA", "EKG", "ECHO", "EYE", "DERM", "DENTA", "MAMMO", "EEG" };

    Checkup NewCheckup(Patient p, string type, DateTime performedAt, decimal price, float? temp)
    {
        var c = new Checkup();
        TrySetIfExists(c, "PatientId", p.Id);
        TrySetIfExists(c, "CheckupType", type);
        TrySetIfExists(c, "PerformedAt", performedAt);
        TrySetIfExists(c, "Price", price);
        TrySetIfExists(c, "BodyTempC", temp);
        EnsureRequiredColumnsHaveValues(model.GetEntity(typeof(Checkup)), c);
        ctx.Set<Checkup>().Add(c);
        checkups.Add(c);
        return c;
    }

    var now = DateTime.UtcNow;

    // For each patient: ~3-4 checkups, different dates/types
    NewCheckup(patients[0], "GP",    now.AddDays(-20), 25m, 36.7f);
    NewCheckup(patients[0], "BLOOD", now.AddDays(-18), 55m, null);
    NewCheckup(patients[0], "EYE",   now.AddDays(-5),  30m, null);
    NewCheckup(patients[0], "EKG",   now.AddDays(-2),  60m, 36.9f);

    NewCheckup(patients[1], "DERM",  now.AddDays(-25), 40m, 37.1f);
    NewCheckup(patients[1], "GP",    now.AddDays(-9),  25m, 36.8f);
    NewCheckup(patients[1], "BLOOD", now.AddDays(-7),  55m, null);

    NewCheckup(patients[2], "GP",    now.AddDays(-30), 25m, 36.6f);
    NewCheckup(patients[2], "CT",    now.AddDays(-28), 250m, null);
    NewCheckup(patients[2], "BLOOD", now.AddDays(-27), 55m, null);
    NewCheckup(patients[2], "ECHO",  now.AddDays(-1),  90m, null);

    NewCheckup(patients[3], "GP",     now.AddDays(-16), 25m, 37.4f);
    NewCheckup(patients[3], "X-RAY",  now.AddDays(-15), 120m, null);
    NewCheckup(patients[3], "ULTRA",  now.AddDays(-10), 110m, null);

    NewCheckup(patients[4], "GP",     now.AddDays(-12), 25m, 36.7f);
    NewCheckup(patients[4], "DERM",   now.AddDays(-11), 40m, null);
    NewCheckup(patients[4], "EYE",    now.AddDays(-3),  30m, null);

    NewCheckup(patients[5], "GP",     now.AddDays(-22), 25m, 36.8f);
    NewCheckup(patients[5], "BLOOD",  now.AddDays(-21), 55m, null);
    NewCheckup(patients[5], "EKG",    now.AddDays(-6),  60m, null);
    NewCheckup(patients[5], "MRI",    now.AddDays(-4),  400m, null);

    NewCheckup(patients[6], "GP",     now.AddDays(-14), 25m, 36.6f);
    NewCheckup(patients[6], "BLOOD",  now.AddDays(-13), 55m, null);
    NewCheckup(patients[6], "CT",     now.AddDays(-8),  250m, null);

    NewCheckup(patients[7], "GP",     now.AddDays(-19), 25m, 36.9f);
    NewCheckup(patients[7], "DENTA",  now.AddDays(-17), 35m, null);
    NewCheckup(patients[7], "ULTRA",  now.AddDays(-9),  110m, null);

    // Save so Checkup.Id exists (required for prescriptions)
    await ctx.SaveChangesAsync();

    // ---------- Prescriptions (~45) ----------
    // Rule of thumb: 1–2 prescriptions per checkup, not all checkups prescribe meds
    var prescriptions = new List<Prescription>();

    Prescription AddRx(Checkup c, Medication m, string dosage, DateTime start, DateTime? end)
    {
        var rx = new Prescription();
        TrySetIfExists(rx, "CheckupId", c.Id);
        TrySetIfExists(rx, "MedicationId", m.Id);
        TrySetIfExists(rx, "Dosage", dosage);
        TrySetIfExists(rx, "StartDate", start.Date);
        TrySetIfExists(rx, "EndDate", end?.Date);
        EnsureRequiredColumnsHaveValues(model.GetEntity(typeof(Prescription)), rx);
        ctx.Set<Prescription>().Add(rx);
        prescriptions.Add(rx);
        return rx;
    }

    // Helper: pick meds by index
    Medication M(int i) => meds[i];

    // A bunch of hand-written realistic patterns
    AddRx(checkups[0],  M(0), "1x daily",      now.AddDays(-20), now.AddDays(-10)); // Paracetamol
    AddRx(checkups[1],  M(1), "Every 8h",      now.AddDays(-18), now.AddDays(-13)); // Ibuprofen
    AddRx(checkups[3],  M(6), "1x daily",      now.AddDays(-2),  null);             // Amlodipine

    AddRx(checkups[4],  M(4), "1x daily",      now.AddDays(-25), now.AddDays(-5));  // Cetirizine
    AddRx(checkups[5],  M(0), "As needed",     now.AddDays(-9),  null);

    AddRx(checkups[7],  M(6), "1x daily",      now.AddDays(-30), null);
    AddRx(checkups[8],  M(2), "1x at night",   now.AddDays(-28), now.AddDays(-14)); // Omeprazole
    AddRx(checkups[10], M(6), "1x daily",      now.AddDays(-1),  null);

    AddRx(checkups[11], M(9), "2x daily",      now.AddDays(-16), now.AddDays(-11)); // Diclofenac
    AddRx(checkups[12], M(9), "Every 12h",     now.AddDays(-15), now.AddDays(-13));

    AddRx(checkups[14], M(4), "1x daily",      now.AddDays(-12), now.AddDays(-7));
    AddRx(checkups[15], M(0), "As needed",     now.AddDays(-11), null);

    AddRx(checkups[17], M(5), "2x daily",      now.AddDays(-22), null);             // Metformin
    AddRx(checkups[18], M(5), "2x daily",      now.AddDays(-21), null);
    AddRx(checkups[20], M(7), "1x daily",      now.AddDays(-6),  null);             // Atorvastatin

    AddRx(checkups[21], M(7), "1x daily",      now.AddDays(-14), null);
    AddRx(checkups[22], M(7), "1x daily",      now.AddDays(-13), null);

    AddRx(checkups[24], M(2), "1x at night",   now.AddDays(-19), now.AddDays(-3));
    AddRx(checkups[25], M(0), "As needed",     now.AddDays(-17), null);

    // Fill out more prescriptions deterministically: for many checkups add one med
    // (still "manual-ish" but gives you volume)
    for (int i = 0; i < checkups.Count; i++)
    {
        // Skip some so it's not 100% saturated
        if (i % 4 == 0) continue;

        var c = checkups[i];
        var med = meds[i % meds.Count];

        // Avoid duplicating exact checkups already handled above (rough rule)
        if (prescriptions.Any(x => x.CheckupId == c.Id)) continue;

        var start = c.PerformedAt is DateTime dt ? dt.Date : now.Date;
        var end = (i % 3 == 0) ? start.AddDays(7) : (DateTime?)null;

        AddRx(c, med, (end is null ? "1x daily" : "2x daily"), start, end);
    }

    await ctx.SaveChangesAsync();

    Console.WriteLine(
        $"Seeded: Patients={patients.Count}, Records={records.Count}, Checkups={checkups.Count}, Meds={meds.Count}, Rx={prescriptions.Count}");
}


    private static async Task CmdSaveAsync(DbContext ctx)
    {
        var affected = await ctx.SaveChangesAsync();
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
        if (args.Count < 2)
        {
            Console.WriteLine("Usage: patients list take N | patients where <expr> [take N]");
            return;
        }

        var sub = args[1].ToLowerInvariant();

        if (sub == "list")
        {
            var take = ReadTakeOrDefault(args, 50);
            var list = await ctx.Set<Patient>()
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .Take(take)
                .ToListAsync();

            foreach (var p in list)
                Console.WriteLine($"{p.Id} {p.FirstName} {p.LastName} {p.Oib}");

            return;
        }

        if (sub == "where")
        {
            if (args.Count < 3)
            {
                Console.WriteLine("Usage: patients where <expr> [take N]");
                Console.WriteLine("Example: patients where LastName = 'Horvat' AND Id > 3 take 20");
                return;
            }

            var exprText = string.Join(" ", args.Skip(2).TakeWhile(x => !x.Equals("take", StringComparison.OrdinalIgnoreCase)));
            var take = ReadTakeOrDefault(args, 50);

            var pred = BuildPredicate<Patient>(exprText);

            var list = await ctx.Set<Patient>()
                .Where(pred)
                .OrderBy(p => p.Id)
                .Take(take)
                .ToListAsync();

            foreach (var p in list)
                Console.WriteLine($"{p.Id} {p.FirstName} {p.LastName} {p.Oib} Phone={p.Phone}");

            return;
        }

        Console.WriteLine("Usage: patients list take N | patients where <expr> [take N]");
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
                var p = await ctx.FindAsync<Patient>(id);
                if (p is null) Console.WriteLine("Not found.");
                else Console.WriteLine($"{p.Id}: {p.FirstName} {p.LastName} ({p.Oib})");
                break;
            }

            case "find":
            {
                if (args.Count < 3)
                {
                    Console.WriteLine("Usage: patient find <text> [take N]");
                    Console.WriteLine("Example: patient find Ivana take 10");
                    return;
                }

                var take = ReadTakeOrDefault(args, 20);

                // Keep the search text as one string
                var text = string.Join(" ", args.Skip(2).TakeWhile(x => !x.Equals("take", StringComparison.OrdinalIgnoreCase)));

                // predicate translator does NOT support Contains/StartsWith,
                // "find" as exact match OR (optional) compare by Id if numeric.
                // possible partial searches via SQL later
                long? asId = long.TryParse(text, out var idv) ? idv : null;

                var q = ctx.Set<Patient>().AsQueryable();

                if (asId.HasValue)
                {
                    q = q.Where(p => p.Id == asId.Value);
                }
                else
                {
                    q = q.Where(p =>
                        p.FirstName == text ||
                        p.LastName == text ||
                        p.Oib == text ||
                        p.Phone == text);
                }

                var list = await q.OrderBy(p => p.Id).Take(take).ToListAsync();
                foreach (var p in list)
                    Console.WriteLine($"{p.Id} {p.FirstName} {p.LastName} {p.Oib} Phone={p.Phone}");

                return;
            }
            
            case "add":
            {
                var model = GetModel(ctx);
                var map = model.GetEntity(typeof(Patient));

                var p = new Patient();

                TrySetIfExists(p, "CreatedAt", DateTime.UtcNow);

                PromptFillFromModel(map, p, isUpdate: false);

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

                var model = GetModel(ctx);
                var map = model.GetEntity(typeof(Patient));

                PromptFillFromModel(map, p, isUpdate: true);

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

            if (rec is null)
            {
                rec = new MedicalRecord();
                TrySetIfExists(rec, "PatientId", pid);
                
                TrySetIfExists(rec, "Notes", ReadNonEmpty("Notes"));

                var model = GetModel(ctx);
                PromptFillFromModel(model.GetEntity(typeof(MedicalRecord)), rec, isUpdate: false);

                ctx.Set<MedicalRecord>().Add(rec);
                Console.WriteLine("Record staged Add. Run 'save'.");
            }
            else
            {
                TrySetIfExists(rec, "Notes", ReadNonEmpty("Notes"));

                var model = GetModel(ctx);
                PromptFillFromModel(model.GetEntity(typeof(MedicalRecord)), rec, isUpdate: true);

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

            case "where":
            {
                if (args.Count < 3)
                {
                    Console.WriteLine("Usage: checkups where <expr> [take N]");
                    Console.WriteLine("Example: checkups where PatientId = 1 AND Price > 50 take 20");
                    return;
                }

                var exprText = string.Join(" ", args.Skip(2).TakeWhile(x => !x.Equals("take", StringComparison.OrdinalIgnoreCase)));
                var take = ReadTakeOrDefault(args, 50);

                var pred = BuildPredicate<Checkup>(exprText);

                var list = await ctx.Set<Checkup>()
                    .Where(pred)
                    .OrderByDescending(c => c.PerformedAt)
                    .Take(take)
                    .ToListAsync();

                foreach (var c in list)
                    Console.WriteLine($"{c.Id} PatientId={c.PatientId} Type={c.CheckupType} At={c.PerformedAt:u} Price={c.Price} Temp={c.BodyTempC}");

                break;
            }

            
            case "add":
            {
                if (args.Count < 3) { Console.WriteLine("Usage: checkups add <patientId>"); return; }
                var pid = long.Parse(args[2]);

                var c = new Checkup();
                TrySetIfExists(c, "PatientId", pid);

                TrySetIfExists(c, "CheckupType", ReadCheckupType());
                TrySetIfExists(c, "Price", ReadDecimal("Price"));
                TrySetIfExists(c, "BodyTempC", ReadNullableFloat("BodyTempC"));
                TrySetIfExists(c, "PerformedAt", DateTime.UtcNow);

                //prompt for any new required columns added later
                var model = GetModel(ctx);
                PromptFillFromModel(model.GetEntity(typeof(Checkup)), c, isUpdate: false);

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
                if (!string.IsNullOrWhiteSpace(p) && decimal.TryParse(p, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                    c.Price = price;

                c.BodyTempC = ReadNullableFloat("BodyTempC");

                // allow updating any new columns you add later
                var model = GetModel(ctx);
                PromptFillFromModel(model.GetEntity(typeof(Checkup)), c, isUpdate: true);

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
            
            case "where":
            {
                if (args.Count < 3)
                {
                    Console.WriteLine("Usage: meds where <expr> [take N]");
                    Console.WriteLine("Example: meds where Id > 5 AND AtcCode = 'N02BE01' take 20");
                    return;
                }

                var exprText = string.Join(" ", args.Skip(2).TakeWhile(x => !x.Equals("take", StringComparison.OrdinalIgnoreCase)));
                var take = ReadTakeOrDefault(args, 50);

                var pred = BuildPredicate<Medication>(exprText);

                var list = await ctx.Set<Medication>()
                    .Where(pred)
                    .OrderBy(m => m.Id)
                    .Take(take)
                    .ToListAsync();

                foreach (var m in list)
                    Console.WriteLine($"{m.Id} {m.Name} ATC={m.AtcCode} Default={m.DefaultDosage}");

                break;
            }

            case "find":
            {
                if (args.Count < 3)
                {
                    Console.WriteLine("Usage: meds find <text> [take N]");
                    Console.WriteLine("Example: meds find Paracetamol take 10");
                    return;
                }

                var take = ReadTakeOrDefault(args, 20);
                var text = string.Join(" ", args.Skip(2).TakeWhile(x => !x.Equals("take", StringComparison.OrdinalIgnoreCase)));

                var list = await ctx.Set<Medication>()
                    .Where(m => m.Name == text || m.AtcCode == text)
                    .OrderBy(m => m.Id)
                    .Take(take)
                    .ToListAsync();

                foreach (var m in list)
                    Console.WriteLine($"{m.Id} {m.Name} ATC={m.AtcCode} Default={m.DefaultDosage}");

                break;
            }
            
            
            case "add":
            {
                var m = new Medication();
                
                TrySetIfExists(m, "Name", ReadNonEmpty("Name (unique)"));
                TrySetIfExists(m, "AtcCode", ReadOptional("ATC code (optional)"));
                TrySetIfExists(m, "DefaultDosage", ReadNonEmpty("Default dosage"));

                // prompt for any new required columns
                var model = GetModel(ctx);
                PromptFillFromModel(model.GetEntity(typeof(Medication)), m, isUpdate: false);

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

                var model = GetModel(ctx);
                PromptFillFromModel(model.GetEntity(typeof(Medication)), m, isUpdate: true);

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

            case "where":
            {
                if (args.Count < 3)
                {
                    Console.WriteLine("Usage: rx where <expr> [take N]");
                    Console.WriteLine("Example: rx where MedicationId = 1 AND EndDate = null take 50");
                    return;
                }

                var exprText = string.Join(" ", args.Skip(2).TakeWhile(x => !x.Equals("take", StringComparison.OrdinalIgnoreCase)));
                var take = ReadTakeOrDefault(args, 100);

                var pred = BuildPredicate<Prescription>(exprText);

                var list = await ctx.Set<Prescription>()
                    .Where(pred)
                    .OrderBy(r => r.Id)
                    .Take(take)
                    .ToListAsync();

                foreach (var r in list)
                    Console.WriteLine($"{r.Id} CheckupId={r.CheckupId} MedId={r.MedicationId} Dosage={r.Dosage} Start={r.StartDate:yyyy-MM-dd} End={(r.EndDate.HasValue ? r.EndDate.Value.ToString("yyyy-MM-dd") : "-")}");

                break;
            }

            
            case "add":
            {
                if (args.Count < 4) { Console.WriteLine("Usage: rx add <checkupId> <medId>"); return; }
                var cid = long.Parse(args[2]);
                var mid = long.Parse(args[3]);

                var r = new Prescription();
                TrySetIfExists(r, "CheckupId", cid);
                TrySetIfExists(r, "MedicationId", mid);

                TrySetIfExists(r, "Dosage", ReadNonEmpty("Dosage"));
                TrySetIfExists(r, "StartDate", DateTime.UtcNow.Date);
                TrySetIfExists(r, "EndDate", ReadOptionalDate("End date yyyy-mm-dd (optional)"));

                // prompt for any new required columns
                var model = GetModel(ctx);
                PromptFillFromModel(model.GetEntity(typeof(Prescription)), r, isUpdate: false);

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

                var model = GetModel(ctx);
                PromptFillFromModel(model.GetEntity(typeof(Prescription)), r, isUpdate: true);

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
    

    private static void PromptFillFromModel(EntityMap map, object entity, bool isUpdate)
    {
        var cols = map.Columns
            .Where(c => !c.IsPrimaryKey && !c.IsIdentity)
            .OrderBy(c => c.Property.Name)
            .ToList();

        foreach (var col in cols)
        {
            if (!isUpdate)
            {
                var existing = col.Property.GetValue(entity);

                if (existing is string s)
                {
                    if (col.IsRequired && !string.IsNullOrWhiteSpace(s))
                        continue;
                }
                else
                {
                    if (col.IsRequired && existing is not null)
                        continue;
                }
            }

            var prop = col.Property;
            var current = isUpdate ? prop.GetValue(entity) : null;

            while (true)
            {
                var label = $"{prop.Name}{(col.IsRequired ? " (required)" : "")}";

                if (isUpdate)
                    Console.Write($"{label} ({FormatValue(current)}): ");
                else
                    Console.Write($"{label}: ");

                var input = Console.ReadLine();

                if (isUpdate && string.IsNullOrWhiteSpace(input))
                    break;

                if (string.IsNullOrWhiteSpace(input))
                {
                    if (!col.IsRequired || IsNullable(prop.PropertyType))
                    {
                        prop.SetValue(entity, null);
                        break;
                    }

                    Console.WriteLine("Value required.");
                    continue;
                }

                if (TryConvert(input.Trim(), prop.PropertyType, out var v, out var err))
                {
                    prop.SetValue(entity, v);
                    break;
                }

                Console.WriteLine(err);
            }
        }

        EnsureRequiredColumnsHaveValues(map, entity);
    }


    private static void EnsureRequiredColumnsHaveValues(EntityMap map, object entity)
    {
        foreach (var col in map.Columns)
        {
            if (col.IsPrimaryKey || col.IsIdentity) continue;
            if (!col.IsRequired) continue;

            var prop = col.Property;
            var val = prop.GetValue(entity);

            if (val is null)
            {
                // fallback so inserts don't fail if a new not null column is added after migration.
                prop.SetValue(entity, FallbackValue(prop.PropertyType, prop.Name));
            }
        }
    }

    private static object? FallbackValue(Type t, string propName)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;

        if (t == typeof(string)) return $"AUTO_{propName}_{DateTime.UtcNow.Ticks}";
        if (t == typeof(int)) return 0;
        if (t == typeof(long)) return 0L;
        if (t == typeof(decimal)) return 0m;
        if (t == typeof(float)) return 0f;
        if (t == typeof(double)) return 0d;
        if (t == typeof(bool)) return false;
        if (t == typeof(DateTime)) return DateTime.UtcNow;
        if (t == typeof(Guid)) return Guid.NewGuid();
        if (t.IsEnum)
        {
            var values = Enum.GetValues(t);
            return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(t);
        }

        return Activator.CreateInstance(t);
    }

    private static bool TryConvert(string input, Type targetType, out object? value, out string error)
    {
        error = "";
        value = null;

        var isNullable = IsNullable(targetType);
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (t == typeof(string))
        {
            value = input;
            return true;
        }

        if (t == typeof(int) && int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
        {
            value = isNullable ? (int?)i : i;
            return true;
        }

        if (t == typeof(long) && long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            value = isNullable ? (long?)l : l;
            return true;
        }

        if (t == typeof(decimal) && decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            value = isNullable ? (decimal?)d : d;
            return true;
        }

        if (t == typeof(float) && float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
        {
            value = isNullable ? (float?)f : f;
            return true;
        }

        if (t == typeof(double) && double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
        {
            value = isNullable ? (double?)db : db;
            return true;
        }

        if (t == typeof(bool) && bool.TryParse(input, out var b))
        {
            value = isNullable ? (bool?)b : b;
            return true;
        }

        if (t == typeof(DateTime) && DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            value = isNullable ? (DateTime?)dt : dt;
            return true;
        }

        if (t == typeof(Guid) && Guid.TryParse(input, out var g))
        {
            value = isNullable ? (Guid?)g : g;
            return true;
        }

        if (t.IsEnum)
        {
            if (Enum.TryParse(t, input, ignoreCase: true, out var ev))
            {
                value = ev;
                return true;
            }

            error = $"Invalid enum value. Allowed: {string.Join(", ", Enum.GetNames(t))}";
            return false;
        }

        try
        {
            value = Convert.ChangeType(input, t, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Invalid value for {t.Name}: {ex.Message}";
            return false;
        }
    }

    private static bool IsNullable(Type t) => Nullable.GetUnderlyingType(t) is not null;

    private static string FormatValue(object? v) => v is null ? "null" : (v.ToString() ?? "");

    private static void TrySetIfExists(object obj, string propName, object? value)
    {
        var p = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (p is null || !p.CanWrite) return;
        p.SetValue(obj, value);
    }

    private static long GetIdOr0(object obj)
    {
        var p = obj.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (p is null) return 0;
        var v = p.GetValue(obj);
        return v is long l ? l : 0;
    }

    private static OrmModel GetModel(DbContext ctx)
    {
        var prop = typeof(DbContext).GetProperty("Model", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (prop?.GetValue(ctx) is OrmModel m) return m;

        var f1 = typeof(DbContext).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f1?.GetValue(ctx) is OrmModel m2) return m2;

        var f2 = typeof(DbContext).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f2?.GetValue(ctx) is OrmModel m3) return m3;

        throw new InvalidOperationException("Cannot access DbContext model.");
    }


    private static IEnumerable<EntityMap> GetAllEntityMaps(OrmModel model)
    {
        var f = model.GetType().GetField("_entities", BindingFlags.Instance | BindingFlags.NonPublic);
        if (f?.GetValue(model) is Dictionary<Type, EntityMap> dict)
            return dict.Values;

        // last resort: empty
        return Enumerable.Empty<EntityMap>();
    }

    // ----------helpers----------

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
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return v;
            Console.WriteLine("Invalid decimal.");
        }
    }

    private static float? ReadNullableFloat(string label)
    {
        Console.Write($"{label} (empty = null): ");
        var s = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        Console.WriteLine("Invalid float, using null.");
        return null;
    }

    private static DateTime? ReadOptionalDate(string label)
    {
        Console.Write($"{label}: ");
        var s = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.Date;
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

            if (string.Equals(s, "X_RAY", StringComparison.OrdinalIgnoreCase)) s = "X-RAY";

            if (allowed.Contains(s))
                return s.ToUpperInvariant();

            Console.WriteLine("Invalid type.");
        }
    }
    
    
    private static int? TryReadInt(List<string> args, string key)
    {
        var idx = args.FindIndex(a => a.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx + 1 >= args.Count) return null;
        return int.TryParse(args[idx + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static List<string> SplitArgs(string input)
    {
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
    
    private static Expression<Func<T, bool>> BuildPredicate<T>(string exprText)
{
    // Grammar (simple):
    //   term := <prop> <op> <value>
    //   expr := term ( (AND|OR) term )*
    //   op   := = != > >= < <=
    // Values: numbers, true/false, null, or 'single-quoted strings'
    //
    // Example:
    //   FirstName = 'Ivana' AND Id > 5
    //
    // NOTE: No parentheses support here;
    var tokens = TokenizeFilter(exprText);
    var param = Expression.Parameter(typeof(T), "e");

    int i = 0;
    Expression ReadTerm()
    {
        if (i + 2 >= tokens.Count) throw new InvalidOperationException("Invalid filter term.");

        var propName = tokens[i++];
        var op = tokens[i++];
        var valueToken = tokens[i++];

        var prop = typeof(T).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop is null) throw new InvalidOperationException($"Unknown property '{propName}'.");

        var left = Expression.Property(param, prop);
        var rightValue = ParseValue(valueToken, prop.PropertyType);
        var right = Expression.Constant(rightValue, prop.PropertyType);

        return op switch
        {
            "="  => Expression.Equal(left, right),
            "!=" => Expression.NotEqual(left, right),
            ">"  => Expression.GreaterThan(left, right),
            ">=" => Expression.GreaterThanOrEqual(left, right),
            "<"  => Expression.LessThan(left, right),
            "<=" => Expression.LessThanOrEqual(left, right),
            _ => throw new InvalidOperationException($"Unsupported operator '{op}'.")
        };
    }

    Expression expr = ReadTerm();

    while (i < tokens.Count)
    {
        var logic = tokens[i++].ToUpperInvariant();
        if (logic != "AND" && logic != "OR") throw new InvalidOperationException("Expected AND/OR.");

        var rhs = ReadTerm();
        expr = logic == "AND" ? Expression.AndAlso(expr, rhs) : Expression.OrElse(expr, rhs);
    }

    return Expression.Lambda<Func<T, bool>>(expr, param);
}

private static List<string> TokenizeFilter(string input)
{
    // Keeps quoted strings together: 'Ana Kovač'
    // Output tokens are: Prop, Op, Value, (AND/OR), Prop, Op, Value...
    var res = new List<string>();
    var cur = "";
    var inQuotes = false;

    for (int j = 0; j < input.Length; j++)
    {
        var ch = input[j];

        if (ch == '\'')
        {
            inQuotes = !inQuotes;
            cur += ch;
            continue;
        }

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

private static object? ParseValue(string token, Type targetType)
{
    // null literal
    if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase))
        return null;

    var t = Nullable.GetUnderlyingType(targetType) ?? targetType;

    // quoted string
    if (token.Length >= 2 && token[0] == '\'' && token[^1] == '\'')
    {
        var s = token.Substring(1, token.Length - 2);
        if (t != typeof(string))
            throw new InvalidOperationException($"Value '{token}' must match type {t.Name}.");
        return s;
    }

    if (t == typeof(string)) return token;
    if (t == typeof(int)) return int.Parse(token, CultureInfo.InvariantCulture);
    if (t == typeof(long)) return long.Parse(token, CultureInfo.InvariantCulture);
    if (t == typeof(decimal)) return decimal.Parse(token, CultureInfo.InvariantCulture);
    if (t == typeof(float)) return float.Parse(token, CultureInfo.InvariantCulture);
    if (t == typeof(double)) return double.Parse(token, CultureInfo.InvariantCulture);
    if (t == typeof(bool)) return bool.Parse(token);
    if (t == typeof(DateTime)) return DateTime.Parse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    // last resort
    return Convert.ChangeType(token, t, CultureInfo.InvariantCulture);
}

private static int ReadTakeOrDefault(List<string> args, int def = 50)
{
    var idx = args.FindIndex(a => a.Equals("take", StringComparison.OrdinalIgnoreCase));
    if (idx < 0 || idx + 1 >= args.Count) return def;
    return int.TryParse(args[idx + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
}

    
    
}
