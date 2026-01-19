namespace ORM.Core.Mapping.Model;

public sealed class OrmModel
{
    private readonly Dictionary<Type, EntityMap> _entities = new();

    public void AddEntity(EntityMap map) => _entities.Add(map.ClrType, map);
    private EntityMap GetEntity(Type t) => _entities[t];
    public EntityMap GetEntity<T>() => GetEntity(typeof(T));
}