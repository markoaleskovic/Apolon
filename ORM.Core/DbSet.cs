namespace ORM.Core;

public sealed class DbSet<T> where T : class
{
    private readonly DbContext _context;

    internal DbSet(DbContext context) => _context = context;

    public void Add(T entity) => _context.Add(entity);

    public Task<T?> FindAsync(object id) => _context.FindAsync<T>(id);

    public Task<List<T>> ToListAsync() => _context.ToListAsync<T>();
}