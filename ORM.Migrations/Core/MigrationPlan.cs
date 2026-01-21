namespace ORM.Migrations.Core;

public sealed class MigrationPlan
{
    public required IReadOnlyList<string> UpSqlStatements { get; init; }
    public required IReadOnlyList<string> DownSqlStatements { get; init; }
}