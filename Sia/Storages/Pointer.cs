namespace Sia;

public readonly record struct Pointer<T>(int Slot, int Version, IStorage<T> Storage) : IDisposable
    where T : struct
{
    public readonly unsafe ref T GetRef()
        => ref Storage.UnsafeGetRef(Slot, Version);
    
    public readonly void Dispose()
        => Storage.UnsafeRelease(Slot, Version);
}