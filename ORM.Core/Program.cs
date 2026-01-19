using ORM.Core;

await using var ctx = new DbContext(
    "Host=localhost;Port=5432;Database=orm_db;Username=postgres;Password=password",
    typeof(Patient)
);

var p = new Patient { FirstName = "Marko" };
ctx.Set<Patient>().Add(p);

await ctx.SaveChangesAsync();
Console.WriteLine($"Inserted id: {p.Id}");