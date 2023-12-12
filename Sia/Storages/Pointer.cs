namespace Sia;

public readonly record struct Pointer<T>(nint Raw, int Version, IStorage<T> Storage) : IDisposable
    where T : struct
{
    public readonly unsafe ref T GetRef()
        => ref Storage.UnsafeGetRef(Raw, Version);
    
    public readonly void Dispose()
        => Storage.UnsafeRelease(Raw, Version);
}