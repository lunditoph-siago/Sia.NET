namespace Sia;

public readonly record struct Pointer<T>(scoped in StorageSlot Slot, IStorage<T> Storage) : IDisposable
    where T : struct
{
    public ref T GetRef()
        => ref Storage.GetRef(Slot);
    
    public void Dispose()
        => Storage.Release(Slot);
}