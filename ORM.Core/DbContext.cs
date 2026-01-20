using System.Linq.Expressions;
using Npgsql;
using ORM.Core.ChangeTracking;
using ORM.Core.LazyLoading;
using ORM.Core.Mapping;
using ORM.Core.Mapping.Model;
using ORM.Core.Materialization;
using ORM.Core.Sql;
using ORM.Core.Querying;

namespace ORM.Core;

public sealed class DbContext : IAsyncDisposable
{
    private readonly string _connectionString;
    private NpgsqlConnection? _conn;
    private NpgsqlTransaction? _tx;

    private readonly OrmModel _model;
    internal OrmModel Model => _model;
    internal ILazyLoader LazyLoader { get; }
    public ChangeTracker ChangeTracker { get; } = new();

    public DbContext(string connStr, params Type[] entityTypes)
    {
        _connectionString = connStr;
        _model = new ModelBuilder(useLazyLoading: true).BuildFrom(entityTypes);
        LazyLoader = new LazyLoader(this, _model);
    }

    public DbSet<T> Set<T>() where T : class => new(this);

    //--------------CRUD METHODS--------------
    internal void Add(object entity)
    {
        var map = _model.GetEntity(entity.GetType());
        ChangeTracker.Track(entity, map, EntityState.Added);
    }

    internal void Attach(object entity)
    {
        var map = _model.GetEntity(entity.GetType());
        ChangeTracker.Track(entity, map, EntityState.Unchanged);
    }

    internal void Update(object entity)
    {
        var map = _model.GetEntity(entity.GetType());
        ChangeTracker.Track(entity, map, EntityState.Modified);
    }

    internal void Remove(object entity)
    {
        var map = _model.GetEntity(entity.GetType());
        ChangeTracker.Track(entity, map, EntityState.Deleted);
    }

    
    
    public async Task<T?> FindAsync<T>(object id) where T : class
    {
        var map = _model.GetEntity<T>();

        var pkCol = map.PrimaryKey.ColumnName;

        //alias columns to ColumnName to match materializer
        var selectCols = string.Join(", ", map.Columns.Select(c =>
            $"{Quote(c.ColumnName)} AS {Quote(c.ColumnName)}"));

        var sql =
            $"SELECT {selectCols} FROM {Quote(map.TableName)} WHERE {Quote(pkCol)} = @p0 LIMIT 1;";

        var conn = await GetOpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn, _tx);
        cmd.Parameters.AddWithValue("p0", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var entity = EntityMaterializer.Materialize<T>(reader, map, LazyLoader);
        
        //track as Unchanged
        ChangeTracker.Track(entity, map, EntityState.Unchanged);

        return entity;
    }
   
    internal async Task<List<T>> QueryAsync<T>(Expression expression) where T : class
    {
        var map = _model.GetEntity<T>();

        //build the parts
        var parts = SqlQueryTranslator.Translate(map, expression);

        // alias columns to ColumnName so materializer can GetOrdinal(ColumnName)
        var selectCols = string.Join(", ", map.Columns.Select(c =>
            $"{Quote(c.ColumnName)} AS {Quote(c.ColumnName)}"));

        var sql =
            $"SELECT {selectCols} FROM {Quote(map.TableName)}" +
            parts.WhereSql + parts.OrderBySql + parts.LimitSql + ";";

        var conn = await GetOpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn, _tx);

        if (parts.Parameters.Length > 0)
            cmd.Parameters.AddRange(parts.Parameters);

        await using var reader = await cmd.ExecuteReaderAsync();

        var result = new List<T>();
        while (await reader.ReadAsync())
        {
            var entity = EntityMaterializer.Materialize<T>(reader, map, LazyLoader);
            ChangeTracker.Track(entity, map, EntityState.Unchanged);
            result.Add(entity);
        }

        return result;
    }
    
    
    //--------------SAVE--------------
   public async Task<int> SaveChangesAsync()
{
    var conn = await GetOpenConnectionAsync();

    // start tx if not already started
    _tx ??= await conn.BeginTransactionAsync();

    var affected = 0;

    try
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                {
                    var (sql, ps, returningPk) = InsertSqlBuilder.Build(entry.Map, entry.Entity);

                    await using var cmd = new NpgsqlCommand(sql, conn, _tx);
                    cmd.Parameters.AddRange(ps);

                    if (returningPk is not null)
                    {
                        var newId = await cmd.ExecuteScalarAsync();
                        SetPkValue(entry.Entity, returningPk, newId);
                        affected++; // count this insert as 1
                    }
                    else
                    {
                        affected += await cmd.ExecuteNonQueryAsync();
                    }

                    entry.State = EntityState.Unchanged;
                    break;
                }

                case EntityState.Modified:
                {
                    var (sql, ps) = UpdateSqlBuilder.Build(entry.Map, entry.Entity);

                    await using var cmd = new NpgsqlCommand(sql, conn, _tx);
                    cmd.Parameters.AddRange(ps);

                    affected += await cmd.ExecuteNonQueryAsync();
                    entry.State = EntityState.Unchanged;
                    break;
                }

                case EntityState.Deleted:
                {
                    var (sql, ps) = DeleteSqlBuilder.Build(entry.Map, entry.Entity);

                    await using var cmd = new NpgsqlCommand(sql, conn, _tx);
                    cmd.Parameters.AddRange(ps);

                    affected += await cmd.ExecuteNonQueryAsync();
                    entry.State = EntityState.Detached;
                    break;
                }

                case EntityState.Unchanged:
                case EntityState.Detached:
                default:
                    break;
            }
        }

        await _tx.CommitAsync();
        await _tx.DisposeAsync();
        _tx = null;

        return affected;
    }
    catch
    {
        if (_tx is not null)
        {
            await _tx.RollbackAsync();
            await _tx.DisposeAsync();
            _tx = null;
        }

        throw;
    }
}


    //--------------HELPERS--------------
    private static string Quote(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
    private static void SetPkValue(object entity, ColumnMap pk, object? dbValue)
    {
        if (dbValue is null || dbValue is DBNull) return;

        var targetType = Nullable.GetUnderlyingType(pk.Property.PropertyType) ?? pk.Property.PropertyType;
        var converted = Convert.ChangeType(dbValue, targetType);
        pk.Property.SetValue(entity, converted);
    }

    
    //--------------CONNECTION--------------
    public async Task<NpgsqlConnection> GetOpenConnectionAsync()
    {
        if (_conn is { State: System.Data.ConnectionState.Open }) return _conn;
        _conn = new NpgsqlConnection(_connectionString);
        await _conn.OpenAsync();
        return _conn;
    }

    public async ValueTask DisposeAsync()
    {
        if (_tx is not null) await _tx.DisposeAsync();
        if (_conn is not null) await _conn.DisposeAsync();
        _tx = null;
        _conn = null;
        GC.SuppressFinalize(this);
    }
}
