namespace Sia;

public interface IStorageHandler<TElement>
    where TElement : struct
{
    void Handle<TStorage>(TStorage stroage)
        where TStorage : IStorage<TElement>, new();
}