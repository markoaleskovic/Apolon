namespace ORM.Migrations.Core;

public static class DiffEngine
{
    public static MigrationPlan Diff(SchemaSnapshot oldSnap, SchemaSnapshot newSnap)
    {
        //build lookup
        var oldTables = oldSnap.Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var newTables = newSnap.Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var up = new List<string>();
        var down = new List<string>();

        //tables added/removed
        var addedTables = newTables.Keys.Except(oldTables.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var removedTables = oldTables.Keys.Except(newTables.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        //create added tables (create table + uniques, but defer FKs)
        foreach (var tName in addedTables.OrderBy(x => x))
        {
            up.AddRange(SqlGen.CreateTableStatements(newTables[tName]));
        }

        //drop added tables (reverse)
        foreach (var tName in addedTables.OrderByDescending(x => x))
        {
            down.Add(SqlGen.DropTableStatement(tName));
        }

        //drop removed tables
        foreach (var tName in removedTables.OrderBy(x => x))
        {
            up.Add(SqlGen.DropTableStatement(tName));
        }

        //recreate removed tables
        foreach (var tName in removedTables.OrderByDescending(x => x))
        {
            down.AddRange(SqlGen.CreateTableStatements(oldTables[tName]));
        }

        //column/unique changes for existing tables
        var commonTables = newTables.Keys.Intersect(oldTables.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var tName in commonTables.OrderBy(x => x))
        {
            var oldT = oldTables[tName];
            var newT = newTables[tName];

            //columns
            var oldCols = oldT.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var newCols = newT.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            var addedCols = newCols.Keys.Except(oldCols.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var removedCols = oldCols.Keys.Except(newCols.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var commonCols = newCols.Keys.Intersect(oldCols.Keys, StringComparer.OrdinalIgnoreCase).ToList();

            //add columns
            foreach (var cName in addedCols.OrderBy(x => x))
                up.Add(SqlGen.AddColumnStatement(newT.Name, newCols[cName]));

            //drop columns added in Up
            foreach (var cName in addedCols.OrderByDescending(x => x))
                down.Add(SqlGen.DropColumnStatement(newT.Name, cName));

            //drop columns removed in new model
            foreach (var cName in removedCols.OrderBy(x => x))
                up.Add(SqlGen.DropColumnStatement(oldT.Name, cName));

            //re-add dropped columns (from old snapshot)
            foreach (var cName in removedCols.OrderByDescending(x => x))
                down.Add(SqlGen.AddColumnStatement(oldT.Name, oldCols[cName]));

            // Detect unsupported modifications
            foreach (var cName in commonCols)
            {
                var o = oldCols[cName];
                var n = newCols[cName];

                var typeChanged = !string.Equals(o.ClrType, n.ClrType, StringComparison.OrdinalIgnoreCase);
                var nullChanged = o.IsNullable != n.IsNullable;
                var identChanged = o.IsIdentity != n.IsIdentity;
                var defChanged = !Equals(NormDefault(o.DefaultValue), NormDefault(n.DefaultValue));



                if (typeChanged || nullChanged || identChanged || defChanged)
                {
                    Console.WriteLine($"DIFF {tName}.{cName}: " +
                                      $"typeChanged={typeChanged}, nullChanged={nullChanged}, identChanged={identChanged}, " +
                                      $"oldDef='{o.DefaultValue ?? "null"}', newDef='{n.DefaultValue ?? "null"}'");

                    /*
                     got errors with this
                     throw new NotSupportedException(
                        $"Column change not supported automatically: {tName}.{cName}. " +
                        "Handle with a manual migration (ALTER COLUMN ...).");*/
                }
            }

            // Uniques (single column uniques)
            var oldU = oldT.Uniques.ToDictionary(u => u.Name, StringComparer.OrdinalIgnoreCase);
            var newU = newT.Uniques.ToDictionary(u => u.Name, StringComparer.OrdinalIgnoreCase);

            var addedU = newU.Keys.Except(oldU.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var removedU = oldU.Keys.Except(newU.Keys, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var uName in addedU.OrderBy(x => x))
                up.Add(SqlGen.AddUniqueStatement(newT.Name, newU[uName]));

            foreach (var uName in addedU.OrderByDescending(x => x))
                down.Add(SqlGen.DropConstraintStatement(newT.Name, uName));

            foreach (var uName in removedU.OrderBy(x => x))
                up.Add(SqlGen.DropConstraintStatement(oldT.Name, uName));

            foreach (var uName in removedU.OrderByDescending(x => x))
                down.Add(SqlGen.AddUniqueStatement(oldT.Name, oldU[uName]));
        }

        //foreign keys
        //build global FK sets by name
        var oldFks = oldSnap.Tables.SelectMany(t => t.ForeignKeys).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var newFks = newSnap.Tables.SelectMany(t => t.ForeignKeys).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        var addedFks = newFks.Keys.Except(oldFks.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        var removedFks = oldFks.Keys.Except(newFks.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        //add new FKs
        foreach (var fkName in addedFks.OrderBy(x => x))
            up.Add(SqlGen.AddForeignKeyStatement(newFks[fkName]));

        //drop those FKs
        foreach (var fkName in addedFks.OrderByDescending(x => x))
        {
            var fk = newFks[fkName];
            down.Add(SqlGen.DropConstraintStatement(fk.DependentTable, fk.Name));
        }

        //drop removed FKs
        foreach (var fkName in removedFks.OrderBy(x => x))
        {
            var fk = oldFks[fkName];
            up.Add(SqlGen.DropConstraintStatement(fk.DependentTable, fk.Name));
        }

        //re-add removed FKs
        foreach (var fkName in removedFks.OrderByDescending(x => x))
            down.Add(SqlGen.AddForeignKeyStatement(oldFks[fkName]));

        //wrap in transaction (each side)
        up = SqlGen.WrapInTransaction(up);
        down = SqlGen.WrapInTransaction(down);
        
        
        

        return new MigrationPlan
        {
            UpSqlStatements = up,
            DownSqlStatements = down
        };
    }

    //helpers
    private static object? NormDefault(object? v)
    {
        if (v is null) return null;
        if (v is string s && s.Length == 0) return null; // treat "" same as null
        return v;
    }


    
    
}
