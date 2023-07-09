namespace Sia;

public class PooledStorage<T> : IStorage<T>
    where T : struct
{
    public int PoolSize { get; set; }
    public IStorage<T> InnerStorage { get; }

    public int Capacity => InnerStorage.Capacity;
    public int Count => InnerStorage.Count;
    public bool IsManaged => InnerStorage.IsManaged;

    private readonly Stack<long> _pooled = new();

    public PooledStorage(int poolSize, IStorage<T> innerStorage)
    {
        PoolSize = poolSize;
        InnerStorage = innerStorage;
    }

    public Pointer<T> Allocate()
    {
        if (!_pooled.TryPop(out var ptr)) {
            ptr = InnerStorage.Allocate().Raw;
        }
        return new(ptr, this);
    }

    public Pointer<T> Allocate(in T initial)
    {
        if (_pooled.TryPop(out var ptr)) {
            InnerStorage.UnsafeGetRef(ptr) = initial;
        }
        else {
            ptr = InnerStorage.Allocate(initial).Raw;
        }
        return new(ptr, this);
    }

    public void UnsafeRelease(long rawPointer)
    {
        if (_pooled.Count < PoolSize) {
            InnerStorage.UnsafeGetRef(rawPointer) = default;
            _pooled.Push(rawPointer);
        }
        else {
            InnerStorage.UnsafeRelease(rawPointer);
        }
    }

    public ref T UnsafeGetRef(long rawPointer)
        => ref InnerStorage.UnsafeGetRef(rawPointer);

    public void Clear()
    {
        foreach (var pointer in _pooled) {
            InnerStorage.UnsafeRelease(pointer);
        }
        _pooled.Clear();
    }
}