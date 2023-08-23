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
    public int PointerValidBits => 64;
    public bool IsManaged => false;

    private HashSet<nint> _allocated = new();

    private static readonly int ElementSize = Unsafe.SizeOf<T>();

    private UnmanagedHeapStorage() {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Pointer<T> Allocate()
        => Allocate(default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe Pointer<T> Allocate(in T initial)
    {
        var ptr = Marshal.AllocHGlobal(ElementSize);
        _allocated.Add(ptr);
        *(T*)ptr = initial;
        Count++;
        return new(ptr, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(long rawPointer)
    {
        var ptr = (nint)rawPointer;
        Marshal.FreeHGlobal(ptr);
        _allocated.Add(ptr);
        Count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref T UnsafeGetRef(long rawPointer)
        => ref *(T*)rawPointer;
    
    public void Dispose()
    {
        foreach (var ptr in _allocated) {
            Marshal.FreeHGlobal(ptr);
        }
        _allocated = null!;
    }
}