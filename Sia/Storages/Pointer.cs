namespace Sia;

public readonly record struct Pointer<T>(scoped in StorageSlot Slot, IStorage<T> Storage) : IDisposable
    where T : struct
{
    public readonly unsafe ref T GetRef()
        => ref Storage.GetRef(Slot);
    
    public readonly void Dispose()
        => Storage.Release(Slot);
}