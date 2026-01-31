using System.Linq.Expressions;
using ORM.Core.Mapping.Model;
using ORM.Core.Querying;

namespace ORM.Core.LazyLoading;

public sealed class LazyLoader : ILazyLoader
{
    private readonly DbContext _ctx;
    private readonly OrmModel _model;

    public LazyLoader(DbContext ctx, OrmModel model)
    {
        _ctx = ctx;
        _model = model;
    }

    //many to one
    public TRelated? LoadReference<TRelated>(object entity, string navigationName) where TRelated : class
    {
        var dependentMap = _model.GetEntity(entity.GetType());

        var rel = dependentMap.Relationships.Single(r =>
            r.Kind == RelationshipKind.ManyToOne &&
            r.NavigationProperty.Name == navigationName);

        var fkValue = rel.ForeignKeyColumn.Property.GetValue(entity);
        if (fkValue is null) return null;

        var principalMap = _model.GetEntity<TRelated>();
        var pkProp = principalMap.PrimaryKey.Property;

        // Build expression: (TRelated x) => x.Pk == fkValue
        var param = Expression.Parameter(typeof(TRelated), "x");
        var left = Expression.Property(param, pkProp.Name);
        var right = Expression.Constant(fkValue, left.Type);
        var body = Expression.Equal(left, right);
        var lambda = Expression.Lambda<Func<TRelated, bool>>(body, param);

        return _ctx.Set<TRelated>().Where(lambda).ToListAsync().GetAwaiter().GetResult().SingleOrDefault();
    }

    //one to many
    public IReadOnlyList<TRelated> LoadCollection<TRelated>(object entity, string navigationName) where TRelated : class
    {
        var principalMap = _model.GetEntity(entity.GetType());

        var rel = principalMap.Relationships.Single(r =>
            r.Kind == RelationshipKind.OneToMany &&
            r.NavigationProperty.Name == navigationName);

        var pkValue = principalMap.PrimaryKey.Property.GetValue(entity);
        if (pkValue is null) return Array.Empty<TRelated>();

        var dependentMap = _model.GetEntity<TRelated>();
        var fkPropName = rel.ForeignKeyColumn.Property.Name;

        // Build expression: (TRelated x) => x.FkProp == pkValue
        var param = Expression.Parameter(typeof(TRelated), "x");
        var left = Expression.Property(param, fkPropName);
        var right = Expression.Constant(pkValue, left.Type);
        var body = Expression.Equal(left, right);
        var lambda = Expression.Lambda<Func<TRelated, bool>>(body, param);

        return _ctx.Set<TRelated>().Where(lambda).ToListAsync().GetAwaiter().GetResult();
    }
}
