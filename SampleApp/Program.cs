using ORM.Core;
using ORM.Core.Querying;
using SampleApp.Models;

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

Lazy-loading demos:
  lazy patient <id>                 - access Patient.MedicalRecord + Patient.Checkups (triggers lazy loads)
  lazy checkup <id>                 - access Checkup.Patient + Checkup.Prescriptions (triggers lazy loads)

Notes:
- Filters supported by your translator are binary comparisons (==, !=, >, >=, <, <=) and && / ||.
- No StartsWith/Contains until you extend PredicateSqlVisitor. 
"""); // reflects current translator limitations [file:18][file:22]
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
            Console.WriteLine("Usage: schema list | schema ping");
            return;
        }

        var sub = args[1].ToLowerInvariant();
        if (sub == "list")
        {
            // No public API to list model; show what we know in this SampleApp
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

        Console.WriteLine("Usage: schema list | schema ping");
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

        await ctx.SaveChangesAsync(); // shows identity PK roundtrip [file:13]

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
        var list = await q.ToListAsync(); // only execution method [file:14][file:22]

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

            // These accessors call LazyLoader.LoadReference/LoadCollection (extra queries). [file:41]
            var rec = p.MedicalRecord;
            Console.WriteLine($"MedicalRecord: {(rec is null ? "(null)" : $"Id={rec.Id} Notes='{rec.Notes}'")}");

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

// ----------------- ENTRYPOINT -----------------