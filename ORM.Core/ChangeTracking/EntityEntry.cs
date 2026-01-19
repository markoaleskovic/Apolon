using ORM.Core.Mapping.Model;

namespace ORM.Core.ChangeTracking;

public sealed class EntityEntry
{
    public required object Entity { get; init; }
    public required EntityMap Map { get; init; }
    public EntityState State { get; set; }
}