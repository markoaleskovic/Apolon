using System.Reflection;
using ORM.Core.Mapping.Attributes;
using ORM.Core.Mapping.Model;

namespace ORM.Core.Mapping;

public sealed class ModelBuilder
{
    private readonly bool _useLazyLoading;
    
    public ModelBuilder(bool useLazyLoading = false)
    {
        _useLazyLoading = useLazyLoading;
    }
    
    public OrmModel BuildFrom(params Type[] entityTypes)
    {
        var model = new OrmModel();
        var maps = new List<EntityMap>();
        foreach (var type in entityTypes)
        {
            var entityMap = BuildEntityMap(type);
            maps.Add(entityMap);
            model.AddEntity(entityMap);
        }

        BuildRelationships(model, maps);
        
        if (_useLazyLoading)
            ValidateLazyLoading(maps);
        
        return model;
    }

    private static EntityMap BuildEntityMap(Type type)
    {
        var tableAttr = type.GetCustomAttribute<TableAttribute>(inherit: false);
        var tableName = tableAttr?.Name ?? type.Name;

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var columns = new List<ColumnMap>();
        ColumnMap? primarykey = null;

        foreach (var p in props)
        {
            if (!p.CanRead || !p.CanWrite) continue;
            if (!IsSimpleColumnType(p.PropertyType)) continue;

            var colAttr = p.GetCustomAttribute<ColumnAttribute>(inherit: false);
            var columnName = colAttr?.Name ?? p.Name;

            var isPrimaryKey = p.IsDefined(typeof(KeyAttribute), inherit: false);
            var isIdentity = p.IsDefined(typeof(DatabaseGeneratedIdentityAttribute), inherit: false);

            if (!isPrimaryKey && string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
                isPrimaryKey = true;

            var isUnique = p.IsDefined(typeof(UniqueAttribute), inherit: false);

            var defaultAttr = p.GetCustomAttribute<DefaultValueAttribute>(inherit: false);
            object? defaultValue = defaultAttr?.Value;

            // Required:
            // - explicit [Required], OR
            // - non-nullable value type (int, DateTime, etc.)
            var isNonNullableValueType =
                p.PropertyType.IsValueType && Nullable.GetUnderlyingType(p.PropertyType) is null;

            var isRequired =
                p.IsDefined(typeof(RequiredAttribute), inherit: false) || isNonNullableValueType;

            var colMap = new ColumnMap(
                columnName,
                p,
                isPrimaryKey,
                isIdentity,
                isRequired,
                isUnique,
                defaultValue
            );

            columns.Add(colMap);

            if (isPrimaryKey)
                primarykey = colMap;
        }

        if (primarykey is null)
            throw new InvalidOperationException($"Entity {type.Name} has no primary key");

        return new EntityMap
        {
            ClrType = type,
            TableName = tableName,
            Columns = columns,
            PrimaryKey = primarykey
        };
    }
    
    
    //yeah, whatever the hell this is man
    private static void BuildRelationships(OrmModel model, List<EntityMap> maps)
{
    var byClr = maps.ToDictionary(m => m.ClrType);

    foreach (var dependent in maps)
    {
        var props = dependent.ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var nav in props)
        {
            // Many-to-one: nav type is another entity type
            if (byClr.TryGetValue(nav.PropertyType, out var principal))
            {
                // require [ForeignKey("SomeId")]
                var fkAttr = nav.GetCustomAttribute<ForeignKeyAttribute>(inherit: false);
                if (fkAttr is null)
                    continue; // maybe throw?

                var fkCol = dependent.Columns.SingleOrDefault(c =>
                    string.Equals(c.Property.Name, fkAttr.ForeignKeyPropertyName, StringComparison.Ordinal));

                if (fkCol is null)
                    throw new InvalidOperationException(
                        $"Foreign key property '{fkAttr.ForeignKeyPropertyName}' not found on {dependent.ClrType.Name}.");

                var inverseAttr = nav.GetCustomAttribute<InversePropertyAttribute>(inherit: false);
                PropertyInfo? inverse = null;
                if (inverseAttr is not null)
                {
                    inverse = principal.ClrType.GetProperty(inverseAttr.InverseNavigationPropertyName,
                        BindingFlags.Public | BindingFlags.Instance);

                    if (inverse is null)
                        throw new InvalidOperationException(
                            $"Inverse navigation '{inverseAttr.InverseNavigationPropertyName}' not found on {principal.ClrType.Name}.");
                }

                dependent.Relationships.Add(new RelationshipMap(
                    RelationshipKind.ManyToOne,
                    principal,
                    dependent,
                    nav,
                    fkCol,
                    inverse
                ));

                continue;
            }

            // One-to-many: ICollection<T> where T is an entity
            if (IsCollectionNavigation(nav))
            {
                var itemType = nav.PropertyType.GetGenericArguments()[0];

                if (!byClr.TryGetValue(itemType, out var manySide))
                    continue;

                // This navigation is on principal (one side), manySide is dependent
                // Need inverse config on collection OR on many side (recommended: on many side with [ForeignKey])
                // We'll accept [InverseProperty] here to find the dependent navigation.
                var inverseAttr = nav.GetCustomAttribute<InversePropertyAttribute>(inherit: false);
                if (inverseAttr is null)
                    continue; // throw?

                var depNav = manySide.ClrType.GetProperty(inverseAttr.InverseNavigationPropertyName,
                    BindingFlags.Public | BindingFlags.Instance);

                if (depNav is null)
                    throw new InvalidOperationException(
                        $"Inverse navigation '{inverseAttr.InverseNavigationPropertyName}' not found on {manySide.ClrType.Name}.");

                var fkAttr = depNav.GetCustomAttribute<ForeignKeyAttribute>(inherit: false);
                if (fkAttr is null)
                    throw new InvalidOperationException(
                        $"Dependent navigation '{manySide.ClrType.Name}.{depNav.Name}' must have [ForeignKey].");

                var fkCol = manySide.Columns.SingleOrDefault(c =>
                    string.Equals(c.Property.Name, fkAttr.ForeignKeyPropertyName, StringComparison.Ordinal));

                if (fkCol is null)
                    throw new InvalidOperationException(
                        $"Foreign key property '{fkAttr.ForeignKeyPropertyName}' not found on {manySide.ClrType.Name}.");

                //minimal: store on principal
                dependent.Relationships.Add(new RelationshipMap(
                    RelationshipKind.OneToMany,
                    dependent,   // principal
                    manySide,    // dependent
                    nav,
                    fkCol,
                    depNav
                ));
            }
        }
    }
}

    //helpers
    private static bool IsSimpleColumnType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;

        return t.IsPrimitive
               || t.IsEnum
               || t == typeof(string)
               || t == typeof(decimal)
               || t == typeof(DateTime)
               || t == typeof(Guid);
    }
    
    private static bool IsCollectionNavigation(PropertyInfo p)
    {
        if (p.PropertyType == typeof(string)) return false;
        if (!p.PropertyType.IsGenericType) return false;

        var gen = p.PropertyType.GetGenericTypeDefinition();
        return gen == typeof(ICollection<>) || gen == typeof(IEnumerable<>) || gen == typeof(List<>);
    }
    
    private static void ValidateLazyLoading(List<EntityMap> maps)
    {
        foreach (var map in maps)
        {
            if (!map.ClrType.IsPublic)
                throw new InvalidOperationException(
                    $"Lazy loading requires public entity types. '{map.ClrType.Name}' is not public.");

            if (map.ClrType.IsSealed)
                throw new InvalidOperationException(
                    $"Lazy loading requires unsealed entity types. '{map.ClrType.Name}' is sealed.");

            foreach (var rel in map.Relationships)
            {
                var nav = rel.NavigationProperty;

                var getter = nav.GetGetMethod();
                if (getter is null)
                    throw new InvalidOperationException(
                        $"Navigation '{map.ClrType.Name}.{nav.Name}' must have a getter for lazy loading.");

                // overridable = IsVirtual && !IsFinal
                if (!getter.IsVirtual || getter.IsFinal)
                    throw new InvalidOperationException(
                        $"Navigation '{map.ClrType.Name}.{nav.Name}' must be virtual for lazy loading proxies.");
            }
        }
    }

    
}
