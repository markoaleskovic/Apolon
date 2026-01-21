namespace ORM.Migrations.Core;

internal static class SqlGen
{
    public static List<string> WrapInTransaction(List<string> statements)
    {
        var list = new List<string> { "BEGIN;" };
        list.AddRange(statements.Where(s => !string.IsNullOrWhiteSpace(s)));
        list.Add("COMMIT;");
        return list;
    }

    public static IEnumerable<string> CreateTableStatements(TableSnapshot t)
    {
        //create table with columns + PK + uniques (FKs handled later)
        var lines = new List<string>();

        var colDefs = new List<string>();
        foreach (var c in t.Columns)
        {
            colDefs.Add($"  \"{c.Name}\" {ToPostgresType(c)}{(c.IsNullable ? "" : " NOT NULL")}{DefaultSql(c)}");
        }

        colDefs.Add($"  CONSTRAINT pk_{t.Name} PRIMARY KEY (\"{t.PrimaryKeyColumn}\")");

        foreach (var u in t.Uniques)
            colDefs.Add($"  CONSTRAINT {u.Name} UNIQUE (\"{u.Column}\")");

        lines.Add($"CREATE TABLE IF NOT EXISTS \"{t.Name}\" (\n{string.Join(",\n", colDefs)}\n);");
        return lines;
    }

    public static string DropTableStatement(string tableName)
        => $"DROP TABLE IF EXISTS \"{tableName}\" CASCADE;";

    public static string AddColumnStatement(string tableName, ColumnSnapshot c)
        => $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{c.Name}\" {ToPostgresType(c)}{(c.IsNullable ? "" : " NOT NULL")}{DefaultSql(c)};";

    public static string DropColumnStatement(string tableName, string columnName)
        => $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\";";

    public static string AddUniqueStatement(string tableName, UniqueSnapshot u)
        => $"ALTER TABLE \"{tableName}\" ADD CONSTRAINT {u.Name} UNIQUE (\"{u.Column}\");";

    public static string AddForeignKeyStatement(ForeignKeySnapshot fk)
        => $"""
ALTER TABLE "{fk.DependentTable}"
ADD CONSTRAINT {fk.Name}
FOREIGN KEY ("{fk.DependentColumn}")
REFERENCES "{fk.PrincipalTable}"("{fk.PrincipalColumn}");
""".TrimEnd();

    public static string DropConstraintStatement(string tableName, string constraintName)
        => $"ALTER TABLE \"{tableName}\" DROP CONSTRAINT IF EXISTS {constraintName};";

    private static string ToPostgresType(ColumnSnapshot c)
    {
        //add here if later I add column attributes such as length reqs
        
        var clr = c.ClrType;

        if (clr.Contains("System.Int64", StringComparison.OrdinalIgnoreCase))
            return c.IsIdentity ? "BIGSERIAL" : "BIGINT";
        if (clr.Contains("System.Int32", StringComparison.OrdinalIgnoreCase))
            return c.IsIdentity ? "SERIAL" : "INT";

        if (clr.Contains("System.String", StringComparison.OrdinalIgnoreCase))
            return "VARCHAR(255)";
        if (clr.Contains("System.Decimal", StringComparison.OrdinalIgnoreCase))
            return "DECIMAL(10,2)";
        if (clr.Contains("System.Single", StringComparison.OrdinalIgnoreCase))
            return "FLOAT";
        if (clr.Contains("System.Double", StringComparison.OrdinalIgnoreCase))
            return "DOUBLE PRECISION";
        if (clr.Contains("System.DateTime", StringComparison.OrdinalIgnoreCase))
            return "TIMESTAMP";
        if (clr.Contains("System.Guid", StringComparison.OrdinalIgnoreCase))
            return "UUID";

        //enum as varchar
        if (clr.Contains("System.Enum", StringComparison.OrdinalIgnoreCase) || clr.Contains(", Enum", StringComparison.OrdinalIgnoreCase))
            return "VARCHAR(64)";

        throw new NotSupportedException($"No SQL type mapping for CLR type: {c.ClrType}");
    }

    private static string DefaultSql(ColumnSnapshot c)
    {
        if (c.DefaultValue is null) return "";
        return $" DEFAULT {FormatDefault(c.DefaultValue)}";
    }

    private static string FormatDefault(object value)
    {
        return value switch
        {
            string s => $"'{s.Replace("'", "''")}'",
            bool b => b ? "TRUE" : "FALSE",
            int i => i.ToString(),
            long l => l.ToString(),
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double db => db.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            _ => $"'{value.ToString()!.Replace("'", "''")}'"
        };
    }
    
    public static List<string> CreateTableStatementsForWholeSchema(SchemaSnapshot snap)
    {
        var list = new List<string>();
        foreach (var t in snap.Tables.OrderBy(t => t.Name))
            list.AddRange(CreateTableStatements(t));

        // add all FKs
        foreach (var fk in snap.Tables.SelectMany(t => t.ForeignKeys).OrderBy(f => f.Name))
            list.Add(AddForeignKeyStatement(fk));

        return list;
    }
    
    
}
