using Npgsql;
using ORM.Core.ChangeTracking;
using ORM.Core.Mapping;
using ORM.Core.Mapping.Model;
using ORM.Core.Sql;

namespace ORM.Core;

public sealed class DbContext : IAsyncDisposable
{
    private readonly string _connectionString;
    private NpgsqlConnection? _conn;
    private NpgsqlTransaction? _tx;

    private readonly OrmModel _model;
    public ChangeTracker ChangeTracker { get; } = new();

    public DbContext(string connStr, params Type[] entityTypes)
    {
        _connectionString = connStr;
        _model = new ModelBuilder().BuildFrom(entityTypes);
    }

    public DbSet<T> Set<T>() where T : class => new(this);

    internal void Add(object entity)
    {
        var map = _model.GetEntity(entity.GetType());
        ChangeTracker.Track(entity, map, EntityState.Added);
    }

    public async Task<int> SaveChangesAsync()
    {
        var conn = await GetOpenConnectionAsync();

        // start tx if not already started
        _tx ??= await conn.BeginTransactionAsync();

        var affected = 0;

        try
        {
            foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
            {
                var (sql, ps, returningPk) = InsertSqlBuilder.Build(entry.Map, entry.Entity);

                await using var cmd = new NpgsqlCommand(sql, conn, _tx);
                cmd.Parameters.AddRange(ps);

                if (returningPk is not null)
                {
                    var newId = await cmd.ExecuteScalarAsync();
                    SetPkValue(entry.Entity, returningPk, newId);
                }
                else
                {
                    affected += await cmd.ExecuteNonQueryAsync();
                }

                entry.State = EntityState.Unchanged;
                affected++;
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

    private static void SetPkValue(object entity, ColumnMap pk, object? dbValue)
    {
        if (dbValue is null || dbValue is DBNull) return;

        var targetType = Nullable.GetUnderlyingType(pk.Property.PropertyType) ?? pk.Property.PropertyType;
        var converted = Convert.ChangeType(dbValue, targetType);
        pk.Property.SetValue(entity, converted);
    }

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
