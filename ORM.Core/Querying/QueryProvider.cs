using System.Linq.Expressions;
using System.Reflection;

namespace ORM.Core.Querying;

internal sealed class QueryProvider : IQueryProvider
{
    private static readonly Type[] DbSetCtorSig =
        { typeof(ORM.Core.DbContext), typeof(IQueryProvider), typeof(Expression) };

    private readonly DbContext _context;
    public QueryProvider(DbContext context) => _context = context;

    public IQueryable CreateQuery(Expression expression)
    {
        var elementType = expression.Type.GetGenericArguments().Single();
        return (IQueryable)CreateDbSet(elementType, expression);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => (IQueryable<TElement>)CreateDbSet(typeof(TElement), expression);

    private object CreateDbSet(Type elementType, Expression expression)
    {
        var setType = typeof(ORM.Core.DbSet<>).MakeGenericType(elementType);

        var ctor = setType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            types: DbSetCtorSig,
            modifiers: null);

        if (ctor is null)
        {
            var available = string.Join(" | ",
                setType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Select(c => "(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.FullName)) + ")"));

            throw new InvalidOperationException(
                $"Expected ctor not found on {setType.AssemblyQualifiedName}. Available: {available}");
        }

        //ensure the args match exactly what ctor expects
        object?[] args = { _context, this, expression };
        return ctor.Invoke(args);
    }

    public object? Execute(Expression expression)
        => throw new NotSupportedException("Use async query execution (ToListAsync).");

    public TResult Execute<TResult>(Expression expression)
        => throw new NotSupportedException("Use async query execution (ToListAsync).");
}
