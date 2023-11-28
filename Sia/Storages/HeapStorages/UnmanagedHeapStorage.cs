#pragma warning disable CS8500

namespace Sia;

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Collections;

public sealed class UnmanagedHeapStorage<T> : IStorage<T>
    where T : struct
{
    public static UnmanagedHeapStorage<T> Instance { get; } = new();

    public int Capacity { get; } = int.MaxValue;
    public int Count { get; private set; }
    public int PointerValidBits => 64;
    public bool IsManaged => false;

    private HashSet<long> _allocated = new();

    private static readonly int ElementSize = Unsafe.SizeOf<T>();

    private UnmanagedHeapStorage() {}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long UnsafeAllocate()
        => UnsafeAllocate(default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe long UnsafeAllocate(in T initial)
    {
        var ptr = Marshal.AllocHGlobal(ElementSize);
        _allocated.Add(ptr);
        *(T*)ptr = initial;
        Count++;
        return ptr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeRelease(long rawPointer)
    {
        var ptr = (nint)rawPointer;
        if (!_allocated.Remove(ptr)) {
            throw new ArgumentException("Invalid pointer");
        }
        Marshal.FreeHGlobal(ptr);
        Count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref T UnsafeGetRef(long rawPointer)
        => ref *(T*)rawPointer;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated(StoragePointerHandler handler)
    {
        foreach (var pointer in _allocated) {
            handler(pointer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IterateAllocated<TData>(in TData data, StoragePointerHandler<TData> handler)
    {
        foreach (var pointer in _allocated) {
            handler(data, pointer);
        }
    }

    public IEnumerator<long> GetEnumerator()
        => _allocated.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
    
    public void Dispose()
    {
        foreach (var ptr in _allocated) {
            Marshal.FreeHGlobal((nint)ptr);
        }
        _allocated = null!;
    }
}