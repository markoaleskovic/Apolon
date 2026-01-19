namespace ORM.Core;

public sealed class DbSet<T> where T : class
{
    private readonly DbContext _context;

    internal DbSet(DbContext context) => _context = context;

    public void Add(T entity) => _context.Add(entity);
}