using ORM.Core.Mapping.Model;

namespace ORM.Core.ChangeTracking;

public sealed class ChangeTracker
{
    private readonly Dictionary<object, EntityEntry> _entries =
        new(ReferenceEqualityComparer.Instance);

    public EntityEntry Track(object entity, EntityMap map, EntityState state)
    {
        if (_entries.TryGetValue(entity, out var entry))
        {
            entry.State = state;
            return entry;
        }

        entry = new EntityEntry { Entity = entity, Map = map, State = state };
        _entries.Add(entity, entry);
        return entry;
    }

    public IEnumerable<EntityEntry> Entries() => _entries.Values;
}