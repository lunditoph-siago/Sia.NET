namespace Sia;

public interface IStorage<T>
    where T : struct
{
    int Capacity { get; }
    int Count { get; }
    bool IsManaged { get; }

    Pointer<T> Allocate();
    Pointer<T> Allocate(in T initial)
    {
        var ptr = Allocate();
        UnsafeGetRef(ptr.Raw) = initial;
        return ptr;
    }

    void UnsafeRelease(long rawPointer);
    ref T UnsafeGetRef(long rawPointer);
}