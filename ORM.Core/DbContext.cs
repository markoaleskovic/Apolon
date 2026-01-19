using Npgsql;
using ORM.Core.Mapping;
using ORM.Core.Mapping.Model;

namespace ORM.Core;

public sealed class DbContext : IAsyncDisposable
{
    private NpgsqlConnection? _npgsqlconnection;
    private readonly string _connectionString;
    private readonly OrmModel _model;
    
    public DbContext(string connectionString, params Type[] entityTypes)
    {
        _connectionString = connectionString;
        _model = new ModelBuilder().BuildFrom(entityTypes);
    }
    
    public async Task<NpgsqlConnection> GetConnectionAsync()
    {
        if (_npgsqlconnection is { State: System.Data.ConnectionState.Open }) return _npgsqlconnection;
        
        _npgsqlconnection = new NpgsqlConnection(_connectionString);
        await  _npgsqlconnection.OpenAsync();
        return _npgsqlconnection;
        
    }

    public async ValueTask DisposeAsync()
    {
        if (_npgsqlconnection is not null) await _npgsqlconnection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public string? GetPostgresVersionAsync()
    {
        return _npgsqlconnection is null ? "Connection not found" : _npgsqlconnection?.PostgreSqlVersion.ToString();
    }
    
    public EntityMap MapFor<T>() => _model.GetEntity<T>();
    
}