namespace Sia;

public static class StorageHelper
{
    public static void CheckPointerValid<T>(in Pointer<T> pointer, IStorage<T> storage)
        where T : struct
    {
        if (pointer.Storage != storage) {
            throw new ArgumentException("Memory was not allocated from this storage");
        }
    }
}