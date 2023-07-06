namespace Sia;

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

public sealed class NativeStorage<T> : IStorage<T>
{
    public static NativeStorage<T> Instance { get; } = new();

    public int Capacity { get; } = int.MaxValue;
    public int Count { get; private set; }

    private static readonly int MemorySize = Unsafe.SizeOf<T>();

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