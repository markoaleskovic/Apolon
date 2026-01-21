namespace ORM.Migrations.Core;

public sealed class SchemaSnapshot
{
    public int Version { get; set; } = 1;
    public List<TableSnapshot> Tables { get; set; } = [];
}

public sealed class TableSnapshot
{
    public string Name { get; set; } = "";
    public string PrimaryKeyColumn { get; set; } = "";
    public List<ColumnSnapshot> Columns { get; set; } = [];
    public List<ForeignKeySnapshot> ForeignKeys { get; set; } = [];
    public List<UniqueSnapshot> Uniques { get; set; } = [];
}

public sealed class ColumnSnapshot
{
    public string Name { get; set; } = "";
    public string ClrType { get; set; } = "";
    public bool IsNullable { get; set; }
    public bool IsIdentity { get; set; }
    public object? DefaultValue { get; set; }
}

public sealed class ForeignKeySnapshot
{
    public string DependentTable { get; set; } = "";
    public string DependentColumn { get; set; } = "";
    public string PrincipalTable { get; set; } = "";
    public string PrincipalColumn { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class UniqueSnapshot
{
    public string Table { get; set; } = "";
    public string Column { get; set; } = "";
    public string Name { get; set; } = "";
}