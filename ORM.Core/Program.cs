using System.Linq;
using System.Reflection;
using ORM.Core;
using ORM.Core.Querying;

static void DumpDbSetCtors<T>() where T : class
{
    var t = typeof(DbSet<T>);
    Console.WriteLine($"DbSet type: {t.AssemblyQualifiedName}");
    Console.WriteLine($"Assembly location: {t.Assembly.Location}");

    var ctors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    foreach (var c in ctors)
    {
        var ps = string.Join(", ", c.GetParameters().Select(p => p.ParameterType.FullName));
        Console.WriteLine($"  ctor: ({ps})");
    }
}


await using var ctx = new DbContext(
    "Host=localhost;Port=5432;Database=orm_db;Username=postgres;Password=password",
    typeof(Patient)
);

Console.WriteLine("=== INSERT (ChangeTracker + SaveChanges) ===");
var a = new Patient { FirstName = "Marko" };
var b = new Patient { FirstName = "Ana" };
var c = new Patient { FirstName = "Marko" };

ctx.Set<Patient>().Add(a);
ctx.Set<Patient>().Add(b);
ctx.Set<Patient>().Add(c);

var affected = await ctx.SaveChangesAsync();
Console.WriteLine($"SaveChanges affected: {affected}");
Console.WriteLine($"Generated IDs: a={a.Id}, b={b.Id}, c={c.Id}");

Console.WriteLine("\n=== FIND (by PK) ===");
var loaded = await ctx.Set<Patient>().FindAsync(a.Id);
Console.WriteLine($"FindAsync: {loaded?.Id} {loaded?.FirstName}");

//DumpDbSetCtors<Patient>();
Console.WriteLine("\n=== QUERY 1: OrderBy + Take ===");
var first5 = await ctx.Set<Patient>()
    .OrderBy(p => p.Id)
    .Take(5)
    .ToListAsync();

foreach (var p in first5)
    Console.WriteLine($"  {p.Id} {p.FirstName}");

Console.WriteLine("\n=== QUERY 2: Where + OrderByDescending + Take ===");
var markos = await ctx.Set<Patient>()
    .Where(p => p.FirstName == "Marko" && p.Id > 0)
    .OrderByDescending(p => p.Id)
    .Take(10)
    .ToListAsync();

foreach (var p in markos)
    Console.WriteLine($"  {p.Id} {p.FirstName}");

Console.WriteLine("\n=== QUERY 3: Complex WHERE (AND/OR) ===");
var complex = await ctx.Set<Patient>()
    .Where(p => (p.FirstName == "Marko" || p.FirstName == "Ana") && p.Id >= 0)
    .OrderBy(p => p.FirstName)
    .ThenBy(p => p.Id)
    .ToListAsync();

foreach (var p in complex)
    Console.WriteLine($"  {p.Id} {p.FirstName}");

Console.WriteLine("\nDone.");