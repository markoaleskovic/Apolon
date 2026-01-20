namespace ORM.Core.Querying;

public static class OrmQueryableExtensions
{
    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) where T : class
    {
        if (source is DbSet<T> dbSet)
            return dbSet.Context.QueryAsync<T>(source.Expression);

        throw new NotSupportedException("Only ORM.Core.DbSet<T> queries are supported.");
    }
}