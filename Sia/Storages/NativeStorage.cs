namespace Sia;

using System.Runtime.InteropServices;

public sealed class NativeStorage<T> : IStorage<T>
{
    public static NativeStorage<T> Instance { get; } = new();

    public int Capacity { get; } = int.MaxValue;
    public int Count { get; private set; }

    private static readonly int MemorySize = Marshal.SizeOf<T>();

    public IntPtr Allocate()
    {
        var ptr = Marshal.AllocHGlobal(MemorySize);
        Count++;
        return ptr;
    }

    public void Release(IntPtr ptr)
    {
        Marshal.FreeHGlobal(ptr);
        Count--;
    }
}