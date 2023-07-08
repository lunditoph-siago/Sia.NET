#pragma warning disable CS8500

namespace Sia;

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

public sealed class UnmanagedHeapStorage<T> : IStorage<T>
    where T : struct
{
    public static UnmanagedHeapStorage<T> Instance { get; } = new();

    public int Capacity { get; } = int.MaxValue;
    public int Count { get; private set; }
    public bool IsManaged => false;

    private static readonly int MemorySize = Unsafe.SizeOf<T>();

    private UnmanagedHeapStorage() {}

    public Pointer<T> Allocate()
        => Allocate(default);

    public unsafe Pointer<T> Allocate(in T initial)
    {
        var ptr = Marshal.AllocHGlobal(MemorySize);
        *(T*)ptr = initial;
        Count++;
        return new(ptr, this);
    }

    public void UnsafeRelease(long rawPointer)
    {
        Marshal.FreeHGlobal((nint)rawPointer);
        Count--;
    }

    public unsafe ref T UnsafeGetRef(long rawPointer)
        => ref *(T*)rawPointer;
}