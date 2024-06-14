namespace Sia;

public interface IStorageTypeHandler<TElement>
    where TElement : struct
{
    void Handle<TStorage>()
        where TStorage : IStorage<TElement>, new();
}