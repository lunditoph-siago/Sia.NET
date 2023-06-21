namespace Sia;

using System.Runtime.InteropServices;

public class NativeStorage<T> : IStorage<T>
{
    public int Capacity { get; } = int.MaxValue;
    public int Count { get; private set; }

    private static readonly int MemorySize = Marshal.SizeOf<T>();

    public virtual IntPtr Allocate()
        => Marshal.AllocHGlobal(MemorySize);

    public virtual void Release(IntPtr ptr)
        => Marshal.FreeHGlobal(ptr);
}