using ORM.Core;

await using var context =
    new DbContext("Host=localhost;Port=5432;Database=orm_db;Username=postgres;Password=password");

await context.GetConnectionAsync();
Console.WriteLine(context.GetPostgresVersionAsync());