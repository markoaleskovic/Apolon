using System.Reflection;

namespace ORM.Core.Mapping.Model;

public sealed record RelationshipMap(
    RelationshipKind Kind,
    EntityMap Principal,                // "one" side (or referenced entity)
    EntityMap Dependent,                // "many" side (or referencing entity)
    PropertyInfo NavigationProperty,    // navigation property on the dependent OR principal (depending on Kind)
    ColumnMap ForeignKeyColumn,         // FK column on dependent
    PropertyInfo? InverseNavigation     //bidirectional mapping
);