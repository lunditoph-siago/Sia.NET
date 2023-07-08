namespace Sia;

public readonly record struct Pointer<T>(long Raw, IStorage<T> Storage) : IDisposable
    where T : struct
{
    public readonly unsafe ref T GetRef()
        => ref Storage.UnsafeGetRef(Raw);
    
    public readonly void Dispose()
        => Storage.UnsafeRelease(Raw);
}