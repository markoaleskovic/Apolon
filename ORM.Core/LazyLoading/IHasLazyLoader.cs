using ORM.Core.LazyLoading;

namespace ORM.Core.LazyLoading;

public interface IHasLazyLoader
{
    void SetLazyLoader(ILazyLoader loader);
}