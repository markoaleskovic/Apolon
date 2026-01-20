namespace ORM.Core.LazyLoading;

public interface ILazyLoader
{
    TRelated? LoadReference<TRelated>(object entity, string navigationName) where TRelated : class;
    IReadOnlyList<TRelated> LoadCollection<TRelated>(object entity, string navigationName) where TRelated : class;
}