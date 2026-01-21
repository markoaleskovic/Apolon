namespace ORM.Core.Mapping.Model;

public sealed class OrmModel
{
    private readonly Dictionary<Type, EntityMap> _entities = new();

    public void AddEntity(EntityMap map) => _entities[map.ClrType] = map;
    public EntityMap GetEntity(Type t)
    {
        return _entities.TryGetValue(t, out var map) ? map : throw new InvalidOperationException($"Entity type '{t.FullName}' is not part of the ORM model.");
    }
    public EntityMap GetEntity<T>() => GetEntity(typeof(T));
    public IReadOnlyList<EntityMap> GetEntities() => _entities.Values.OrderBy(e => e.TableName).ToList();
}