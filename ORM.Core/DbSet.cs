using System.Collections;
using System.Linq.Expressions;
using ORM.Core.Querying;

namespace ORM.Core;

public sealed class DbSet<T> : IOrderedQueryable<T> where T : class
{
    private readonly DbContext _context;
    private readonly Expression _expression;
    private readonly IQueryProvider _provider;
    internal DbContext Context => _context;
    
    internal DbSet(DbContext context)
    {
        _context = context;
        _provider = new QueryProvider(context);
        _expression = Expression.Constant(this);
    }

    internal DbSet(DbContext context, IQueryProvider provider, Expression expression)
    {
        _context = context;
        _provider = provider;
        _expression = expression;
    }

    public Type ElementType => typeof(T);
    public Expression Expression => _expression;
    public IQueryProvider Provider => _provider;

    public IEnumerator<T> GetEnumerator() =>
        throw new NotSupportedException("Use ToListAsync() to execute queries.");

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    //CRUD helpers
    public void Add(T entity) => _context.Add(entity);
    public Task<T?> FindAsync(object id) => _context.FindAsync<T>(id);
    public void Update(T entity) => _context.Update(entity);
    public void Remove(T entity) => _context.Remove(entity);
    public void Attach(T entity) => _context.Attach(entity);

    //terminal execution
    public Task<List<T>> ToListAsync() => _context.QueryAsync<T>(_expression);
}